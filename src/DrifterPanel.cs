using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FlotsamTrainer
{
    internal static class DrifterTools
    {
        private static readonly Action<AgentDescriptor, DrifterAttributesEffect> SetBackground =
            (Action<AgentDescriptor, DrifterAttributesEffect>)AccessTools.PropertySetter(
                typeof(AgentDescriptor), nameof(AgentDescriptor.PresentBackground))
                .CreateDelegate(typeof(Action<AgentDescriptor, DrifterAttributesEffect>));
        private static readonly Action<Agent, DrifterAttributesEffect> RemoveBackground =
            (Action<Agent, DrifterAttributesEffect>)AccessTools.Method(typeof(Agent), "RemoveBackground")
                .CreateDelegate(typeof(Action<Agent, DrifterAttributesEffect>));
        private static readonly Action<Agent> ApplyBackground =
            (Action<Agent>)AccessTools.Method(typeof(Agent), "ApplyPresentBackground").CreateDelegate(typeof(Action<Agent>));

        internal static bool CanEdit(Agent agent) =>
            !LoadingScreen.IsLoading && agent != null && agent.Initialized && agent.IsAlive &&
            Community.PlayerCommunity != null && agent.Community == Community.PlayerCommunity &&
            agent.Attributes != null && agent.Descriptor != null;

        internal static void SetWorkPreference(Agent agent, DrifterAttributesEffect background)
        {
            if (!CanEdit(agent) || background == null || background == agent.Descriptor.PresentBackground)
                return;

            bool allowed = false;
            foreach (var candidate in agent.Properties.PresentBackgrounds)
                allowed |= candidate == background;
            if (!allowed)
                return;

            var previous = agent.Descriptor.PresentBackground;
            RemoveBackground(agent, previous);
            SetBackground(agent.Descriptor, background);
            try
            {
                ApplyBackground(agent);
            }
            catch
            {
                RemoveBackground(agent, background);
                SetBackground(agent.Descriptor, previous);
                ApplyBackground(agent);
                throw;
            }
            agent.OnAgentUpdated.Invoke();
            agent.Community.TriggerAgentUpdatedEvents();
            TownTools.RefreshWorkerSpeed();
        }
    }

    public sealed partial class TrainerPanel
    {
        private const int DriftersPerPage = 5;
        private const int PreferenceRows = 6;
        private const int DietRows = 6;
        private readonly List<Agent> visibleDrifters = new List<Agent>();
        private readonly List<DrifterAttributesEffect> workPreferences = new List<DrifterAttributesEffect>();
        private readonly List<DrifterAttributes.Attribute> skillAttributes = new List<DrifterAttributes.Attribute>();
        private readonly Button[] drifterButtons = new Button[DriftersPerPage];
        private readonly Text[] drifterNames = new Text[DriftersPerPage];
        private readonly Text[] drifterLevels = new Text[DriftersPerPage];
        private readonly RawImage[] drifterPortraits = new RawImage[DriftersPerPage];
        private readonly List<InputField> skillInputs = new List<InputField>();
        private readonly List<Text> skillLabels = new List<Text>();
        private readonly List<Image> skillIcons = new List<Image>();
        private readonly Button[] preferenceButtons = new Button[PreferenceRows];
        private readonly Text[] preferenceNames = new Text[PreferenceRows];
        private readonly Text[] preferenceDetails = new Text[PreferenceRows];
        private readonly Image[] preferenceIcons = new Image[PreferenceRows];
        private readonly Button[] foodPriorityButtons = new Button[DietRows];
        private readonly Button[] foodFavouriteButtons = new Button[DietRows];
        private readonly Text[] foodNames = new Text[DietRows];
        private readonly Text[] foodPriorities = new Text[DietRows];
        private readonly Image[] foodIcons = new Image[DietRows];
        private readonly List<Diet.Entry> dietEntries = new List<Diet.Entry>();
        private GameObject drifterPage;
        private Graphic drifterTab;
        private GameObject skillsPage;
        private GameObject preferencesPage;
        private GameObject foodPage;
        private Graphic skillsTab;
        private Graphic preferencesTab;
        private Graphic foodTab;
        private InputField drifterSearch;
        private Text drifterPageLabel;
        private Text drifterName;
        private Text drifterSubtitle;
        private Text drifterStatus;
        private Text preferencePageLabel;
        private Text currentPreference;
        private Text dietPageLabel;
        private Text favouriteFood;
        private RawImage selectedPortrait;
        private Agent selectedDrifter;
        private Community drifterTown;
        private int drifterPageIndex;
        private int preferencePageIndex;
        private int dietPageIndex;
        private float nextDrifterRefresh;

        private void CreateDrifterPage(RectTransform frame)
        {
            var rect = new GameObject("Drifters", typeof(RectTransform)).GetComponent<RectTransform>();
            Position(rect, frame, new Rect(0, 0, 620, 580));
            drifterPage = rect.gameObject;
            drifterSearch = AddInput(rect, new Rect(18, 112, 150, 30), InputField.ContentType.Standard, 32);
            drifterSearch.placeholder = Label((RectTransform)drifterSearch.transform, "搜索漂流者",
                new Rect(9, 0, 132, 30), 16, new Color32(99, 117, 116, 255));
            drifterSearch.onValueChanged.AddListener(_ =>
            {
                drifterPageIndex = 0;
                RefreshDrifterRoster();
            });
            for (int i = 0; i < DriftersPerPage; i++)
            {
                int slot = i;
                var button = AddButton(rect, "", new Rect(18, 154 + i * 75, 150, 67), ButtonColor,
                    () => SelectDrifterSlot(slot));
                drifterButtons[i] = button;
                var row = (RectTransform)button.transform;
                drifterPortraits[i] = AddPortrait(row, new Rect(3, 5, 51, 57));
                drifterNames[i] = Label(row, "", new Rect(58, 9, 87, 27), 16, Ink);
                drifterNames[i].resizeTextForBestFit = true;
                drifterNames[i].resizeTextMinSize = 11;
                drifterLevels[i] = Label(row, "", new Rect(58, 37, 87, 22), 13, AccentColor);
            }
            AddButton(rect, "‹", new Rect(18, 535, 31, 29), ButtonColor, () => ChangeDrifterPage(-1));
            drifterPageLabel = Label(rect, "", new Rect(52, 535, 80, 29), 15, Ink, TextAnchor.MiddleCenter);
            AddButton(rect, "›", new Rect(137, 535, 31, 29), ButtonColor, () => ChangeDrifterPage(1));

            var sheet = AddImage(rect, new Rect(181, 112, 421, 452), FieldColor);
            UseSlicedArt(sheet, controlArt);
            selectedPortrait = AddPortrait(rect, new Rect(190, 115, 58, 68));
            drifterName = Label(rect, "", new Rect(255, 117, 330, 30), 23, Ink);
            drifterSubtitle = Label(rect, "", new Rect(255, 149, 330, 30), 15, AccentColor);
            skillsTab = AddButton(rect, "专长", new Rect(192, 192, 123, 31), TitleColor,
                () => SelectDrifterSection(0)).targetGraphic;
            preferencesTab = AddButton(rect, "工作喜好", new Rect(326, 192, 123, 31), ButtonColor,
                () => SelectDrifterSection(1)).targetGraphic;
            foodTab = AddButton(rect, "饮食食谱", new Rect(460, 192, 130, 31), ButtonColor,
                () => SelectDrifterSection(2)).targetGraphic;
            CreateSkillsPage(rect);
            CreatePreferencesPage(rect);
            CreateDietPage(rect);
            drifterStatus = Label(rect, "选择漂流者后编辑", new Rect(18, 587, 425, 28), 15, AccentColor);
            SelectDrifterSection(0);
            drifterPage.SetActive(false);
        }

        private GameObject CreateDrifterSection(RectTransform parent, string name)
        {
            var section = new GameObject(name, typeof(RectTransform));
            Position((RectTransform)section.transform, parent, new Rect(0, 0, 620, 580));
            return section;
        }

        private void CreateSkillsPage(RectTransform parent)
        {
            skillsPage = CreateDrifterSection(parent, "Skills");
            var rect = (RectTransform)skillsPage.transform;
            var types = DrifterAttributes.ReturnAttributeTypes();
            int index = 0;
            foreach (var type in types)
            {
                if (type == DrifterAttributes.AttributeType.None)
                    continue;
                int x = 193 + index % 2 * 201;
                int y = 236 + index / 2 * 40;
                skillIcons.Add(AddSprite(rect, new Rect(x, y + 5, 23, 23), null));
                skillLabels.Add(Label(rect, "", new Rect(x + 28, y, 109, 32), 15, Ink));
                skillInputs.Add(AddInput(rect, new Rect(x + 139, y, 49, 32), InputField.ContentType.IntegerNumber, 3));
                index++;
            }
            AddButton(rect, "填入上限", new Rect(194, 522, 121, 30), ButtonColor, FillSkillMaximum);
            AddButton(rect, "应用专长", new Rect(460, 522, 128, 30), TitleColor, ApplyDrifterSkills);
            Label(rect, "数值为可分配专长", new Rect(322, 522, 134, 30), 13, AccentColor);
        }

        private void CreatePreferencesPage(RectTransform parent)
        {
            preferencesPage = CreateDrifterSection(parent, "Preferences");
            var rect = (RectTransform)preferencesPage.transform;
            currentPreference = Label(rect, "", new Rect(194, 233, 395, 28), 16, Ink);
            for (int i = 0; i < PreferenceRows; i++)
            {
                int slot = i;
                var button = AddButton(rect, "", new Rect(194, 267 + i * 39, 394, 35), ButtonColor,
                    () => ApplyWorkPreference(slot));
                preferenceButtons[i] = button;
                var row = (RectTransform)button.transform;
                preferenceIcons[i] = AddSprite(row, new Rect(7, 5, 25, 25), null);
                preferenceNames[i] = Label(row, "", new Rect(39, 0, 128, 35), 15, Ink);
                preferenceDetails[i] = Label(row, "", new Rect(169, 0, 220, 35), 13, AccentColor);
            }
            AddButton(rect, "‹", new Rect(194, 515, 32, 28), ButtonColor, () => ChangePreferencePage(-1));
            preferencePageLabel = Label(rect, "", new Rect(230, 515, 320, 28), 14, Ink, TextAnchor.MiddleCenter);
            AddButton(rect, "›", new Rect(556, 515, 32, 28), ButtonColor, () => ChangePreferencePage(1));
            Label(rect, "点击选择原生喜好 · 随游戏保存", new Rect(194, 545, 394, 18), 12, AccentColor);
        }

        private void CreateDietPage(RectTransform parent)
        {
            foodPage = CreateDrifterSection(parent, "Diet");
            var rect = (RectTransform)foodPage.transform;
            favouriteFood = Label(rect, "", new Rect(194, 233, 395, 28), 16, Ink);
            for (int i = 0; i < DietRows; i++)
            {
                int slot = i;
                int y = 267 + i * 39;
                foodIcons[i] = AddSprite(rect, new Rect(196, y + 3, 29, 29), null);
                foodNames[i] = Label(rect, "", new Rect(233, y, 161, 35), 15, Ink);
                var priority = AddButton(rect, "", new Rect(400, y + 2, 109, 31), ButtonColor,
                    () => CycleFoodPriority(slot));
                foodPriorityButtons[i] = priority;
                foodPriorities[i] = priority.GetComponentInChildren<Text>();
                foodFavouriteButtons[i] = AddButton(rect, "最爱", new Rect(517, y + 2, 71, 31), ButtonColor,
                    () => SetFavouriteFood(slot));
            }
            AddButton(rect, "‹", new Rect(194, 515, 32, 28), ButtonColor, () => ChangeDietPage(-1));
            dietPageLabel = Label(rect, "", new Rect(230, 515, 320, 28), 14, Ink, TextAnchor.MiddleCenter);
            AddButton(rect, "›", new Rect(556, 515, 32, 28), ButtonColor, () => ChangeDietPage(1));
            Label(rect, "点击切换 禁用 / 低 / 普通 / 高", new Rect(194, 545, 394, 18), 12, AccentColor);
        }

        private static RawImage AddPortrait(RectTransform parent, Rect bounds)
        {
            var portrait = new GameObject("Native portrait", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            Position(portrait.rectTransform, parent, bounds);
            portrait.raycastTarget = false;
            portrait.enabled = false;
            return portrait;
        }

        private static void RefreshPortrait(RawImage image, Agent agent)
        {
            bool ready = DrifterTools.CanEdit(agent) && PortraitGenerator.HasStaticPortrait(agent.Descriptor);
            image.texture = ready ? PortraitGenerator.ReturnStaticPortrait(agent.Descriptor) : null;
            image.enabled = image.texture != null;
        }

        private void ShowDrifters()
        {
            featurePage.SetActive(false);
            itemPage.SetActive(false);
            drifterPage.SetActive(true);
            featureTab.color = itemTab.color = ButtonColor;
            drifterTab.color = TitleColor;
            RefreshDrifterPage();
        }

        private void SelectDrifterSection(int section)
        {
            skillsPage.SetActive(section == 0);
            preferencesPage.SetActive(section == 1);
            foodPage.SetActive(section == 2);
            skillsTab.color = section == 0 ? TitleColor : ButtonColor;
            preferencesTab.color = section == 1 ? TitleColor : ButtonColor;
            foodTab.color = section == 2 ? TitleColor : ButtonColor;
        }

        private void UpdateDrifterPage()
        {
            if (drifterPage == null || !drifterPage.activeInHierarchy || Time.unscaledTime < nextDrifterRefresh)
                return;
            nextDrifterRefresh = Time.unscaledTime + 0.75f;
            if (drifterTown != Community.PlayerCommunity || !DrifterTools.CanEdit(selectedDrifter))
                RefreshDrifterPage();
            else
                RefreshDrifterRoster();
        }

        private void RefreshDrifterPage()
        {
            var town = LoadingScreen.IsLoading ? null : Community.PlayerCommunity;
            if (drifterTown != town || !DrifterTools.CanEdit(selectedDrifter))
                selectedDrifter = null;
            drifterTown = town;
            RefreshDrifterRoster();
            if (selectedDrifter == null && visibleDrifters.Count > 0)
                selectedDrifter = visibleDrifters[Mathf.Min(drifterPageIndex * DriftersPerPage, visibleDrifters.Count - 1)];
            RefreshDrifterDetails();
            RefreshDrifterRoster();
        }

        private void RefreshDrifterRoster()
        {
            visibleDrifters.Clear();
            var town = LoadingScreen.IsLoading ? null : Community.PlayerCommunity;
            if (town != null)
                foreach (var agent in town.Agents)
                    if (DrifterTools.CanEdit(agent) &&
                        agent.Name.IndexOf(drifterSearch.text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                        visibleDrifters.Add(agent);
            int pages = Math.Max(1, (visibleDrifters.Count + DriftersPerPage - 1) / DriftersPerPage);
            drifterPageIndex = Mathf.Clamp(drifterPageIndex, 0, pages - 1);
            drifterPageLabel.text = $"{drifterPageIndex + 1} / {pages}";
            for (int i = 0; i < DriftersPerPage; i++)
            {
                int index = drifterPageIndex * DriftersPerPage + i;
                bool valid = index < visibleDrifters.Count;
                drifterButtons[i].gameObject.SetActive(valid);
                if (!valid)
                    continue;
                var agent = visibleDrifters[index];
                drifterButtons[i].targetGraphic.color = agent == selectedDrifter ? TitleColor : ButtonColor;
                drifterNames[i].text = agent.Name;
                drifterLevels[i].text = $"等级 {agent.Attributes.Level}";
                RefreshPortrait(drifterPortraits[i], agent);
            }
            RefreshPortrait(selectedPortrait, selectedDrifter);
        }

        private void ChangeDrifterPage(int direction)
        {
            drifterPageIndex += direction;
            RefreshDrifterRoster();
        }

        private void SelectDrifterSlot(int slot)
        {
            int index = drifterPageIndex * DriftersPerPage + slot;
            if (index >= visibleDrifters.Count || !DrifterTools.CanEdit(visibleDrifters[index]))
                return;
            selectedDrifter = visibleDrifters[index];
            preferencePageIndex = dietPageIndex = 0;
            RefreshDrifterDetails();
            RefreshDrifterRoster();
        }

        private void RefreshDrifterDetails()
        {
            bool ready = DrifterTools.CanEdit(selectedDrifter);
            drifterName.text = ready ? selectedDrifter.Name : "暂无漂流者";
            drifterSubtitle.text = ready ? $"等级 {selectedDrifter.Attributes.Level} · 专长上限 {selectedDrifter.Attributes.MaximumAttributeLevel}" :
                "进入城镇后选择人物";
            drifterStatus.text = ready ? "修改仅作用于当前人物 · 随游戏保存" : "进入城镇后可编辑漂流者";
            RefreshPortrait(selectedPortrait, selectedDrifter);
            skillAttributes.Clear();
            if (ready)
                foreach (var attribute in selectedDrifter.Attributes.Attributes)
                    if (attribute.Type != DrifterAttributes.AttributeType.None)
                        skillAttributes.Add(attribute);
            for (int i = 0; i < skillInputs.Count; i++)
            {
                bool exists = i < skillAttributes.Count;
                skillInputs[i].gameObject.SetActive(exists);
                skillLabels[i].gameObject.SetActive(exists);
                skillIcons[i].gameObject.SetActive(exists);
                if (!exists)
                    continue;
                var attribute = skillAttributes[i];
                skillLabels[i].text = attribute.Name.ToString();
                skillIcons[i].sprite = attribute.Icon;
                skillIcons[i].enabled = attribute.Icon != null;
                skillInputs[i].SetTextWithoutNotify(attribute.Expertise.ToString(CultureInfo.InvariantCulture));
            }
            workPreferences.Clear();
            dietEntries.Clear();
            if (ready)
            {
                foreach (var preference in selectedDrifter.Properties.PresentBackgrounds)
                    if (preference != null)
                        workPreferences.Add(preference);
                if (selectedDrifter.Vitals.TryReturnDiet(VitalType.Hunger, out var diet))
                    dietEntries.AddRange(diet.Entries);
            }
            RefreshPreferences();
            RefreshDiet();
        }

        private void FillSkillMaximum()
        {
            if (!DrifterTools.CanEdit(selectedDrifter))
                return;
            foreach (var input in skillInputs)
                input.SetTextWithoutNotify(selectedDrifter.Attributes.MaximumAttributeLevel.ToString(CultureInfo.InvariantCulture));
        }

        private void ApplyDrifterSkills()
        {
            if (!DrifterTools.CanEdit(selectedDrifter))
            {
                RefreshDrifterPage();
                return;
            }
            var values = new int[skillAttributes.Count];
            for (int i = 0; i < values.Length; i++)
                if (!int.TryParse(skillInputs[i].text, out values[i]) || values[i] < 0 ||
                    values[i] > selectedDrifter.Attributes.MaximumAttributeLevel)
                {
                    drifterStatus.text = $"专长请输入 0–{selectedDrifter.Attributes.MaximumAttributeLevel}";
                    return;
                }
            for (int i = 0; i < values.Length; i++)
                selectedDrifter.Attributes.SetExpertise(skillAttributes[i].Type, values[i]);
            TownTools.RefreshWorkerSpeed();
            drifterStatus.text = $"已更新 {selectedDrifter.Name} 的专长";
        }

        private static string PreferenceName(DrifterAttributesEffect preference) =>
            preference == null ? "无" : preference.Name.ToString();

        private string PreferenceSummary(DrifterAttributesEffect preference)
        {
            var parts = new List<string>();
            if (preference.Modifiers != null && DrifterTools.CanEdit(selectedDrifter))
                foreach (var modifier in preference.Modifiers)
                    if (modifier.Affinity != 0)
                        parts.Add($"{selectedDrifter.Attributes.ReturnAttributeName(modifier.Type)} {modifier.Affinity:+0;-0;0}");
            return parts.Count == 0 ? "无工作喜好加成" : string.Join(" · ", parts);
        }

        private void RefreshPreferences()
        {
            bool ready = DrifterTools.CanEdit(selectedDrifter);
            currentPreference.text = ready ? "当前 · " + PreferenceName(selectedDrifter.Descriptor.PresentBackground) : "选择人物后设置";
            int pages = Math.Max(1, (workPreferences.Count + PreferenceRows - 1) / PreferenceRows);
            preferencePageIndex = Mathf.Clamp(preferencePageIndex, 0, pages - 1);
            preferencePageLabel.text = $"{preferencePageIndex + 1} / {pages}";
            for (int i = 0; i < PreferenceRows; i++)
            {
                int index = preferencePageIndex * PreferenceRows + i;
                bool exists = ready && index < workPreferences.Count;
                preferenceButtons[i].gameObject.SetActive(exists);
                if (!exists)
                    continue;
                var preference = workPreferences[index];
                preferenceNames[i].text = PreferenceName(preference);
                preferenceDetails[i].text = PreferenceSummary(preference);
                preferenceIcons[i].sprite = preference.IconProperties?.Sprite;
                preferenceIcons[i].enabled = preferenceIcons[i].sprite != null;
                preferenceButtons[i].targetGraphic.color = selectedDrifter.Descriptor.PresentBackground == preference ? TitleColor : ButtonColor;
            }
        }

        private void ApplyWorkPreference(int slot)
        {
            int index = preferencePageIndex * PreferenceRows + slot;
            if (!DrifterTools.CanEdit(selectedDrifter) || index >= workPreferences.Count)
                return;
            DrifterTools.SetWorkPreference(selectedDrifter, workPreferences[index]);
            RefreshPreferences();
            drifterStatus.text = $"已更新 {selectedDrifter.Name} 的工作喜好";
        }

        private void ChangePreferencePage(int direction)
        {
            preferencePageIndex += direction;
            RefreshPreferences();
        }

        private Diet SelectedDiet()
        {
            if (DrifterTools.CanEdit(selectedDrifter) && selectedDrifter.Vitals.TryReturnDiet(VitalType.Hunger, out var diet))
                return diet;
            return null;
        }

        private void RefreshDiet()
        {
            var diet = SelectedDiet();
            favouriteFood.text = diet?.Favourite != null ? "最爱 · " + ItemName(diet.Favourite.ItemProperties) : "最爱 · 暂无";
            int pages = Math.Max(1, (dietEntries.Count + DietRows - 1) / DietRows);
            dietPageIndex = Mathf.Clamp(dietPageIndex, 0, pages - 1);
            dietPageLabel.text = $"{dietPageIndex + 1} / {pages}";
            for (int i = 0; i < DietRows; i++)
            {
                int index = dietPageIndex * DietRows + i;
                bool exists = diet != null && index < dietEntries.Count;
                foodNames[i].gameObject.SetActive(exists);
                foodIcons[i].gameObject.SetActive(exists);
                foodPriorityButtons[i].gameObject.SetActive(exists);
                foodFavouriteButtons[i].gameObject.SetActive(exists);
                if (!exists)
                    continue;
                var entry = dietEntries[index];
                foodNames[i].text = ItemName(entry.ItemProperties);
                foodIcons[i].sprite = entry.ItemProperties.InventorySprite;
                foodIcons[i].enabled = foodIcons[i].sprite != null;
                foodPriorities[i].text = new[] { "禁用", "低优先", "普通", "高优先" }[(int)entry.Priority];
                foodPriorityButtons[i].targetGraphic.color = entry.Priority == AssignmentPriority.None ? WarningColor : ButtonColor;
                foodFavouriteButtons[i].targetGraphic.color = diet.Favourite == entry ? TitleColor : ButtonColor;
            }
        }

        private void CycleFoodPriority(int slot)
        {
            var diet = SelectedDiet();
            int index = dietPageIndex * DietRows + slot;
            if (diet == null || index >= dietEntries.Count)
                return;
            var entry = dietEntries[index];
            diet.SetPriority(entry.ItemProperties, (AssignmentPriority)(((int)entry.Priority + 1) % 4));
            RefreshDiet();
            drifterStatus.text = $"已更新 {selectedDrifter.Name} 的食谱";
        }

        private void SetFavouriteFood(int slot)
        {
            var diet = SelectedDiet();
            int index = dietPageIndex * DietRows + slot;
            if (diet == null || index >= dietEntries.Count)
                return;
            var entry = dietEntries[index];
            if (entry.Priority == AssignmentPriority.None)
                diet.SetPriority(entry.ItemProperties, AssignmentPriority.Default);
            diet.SetFavourite(entry.ItemProperties);
            GameEventDispatcher.Dispatch(GameEventType.AgentDietUpdated);
            RefreshDiet();
            drifterStatus.text = $"已设置 {selectedDrifter.Name} 的最爱食物";
        }

        private void ChangeDietPage(int direction)
        {
            dietPageIndex += direction;
            RefreshDiet();
        }
    }
}
