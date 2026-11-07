using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FlotsamTrainer
{
    public sealed partial class TrainerPanel : MonoBehaviour
    {
        private const string PatchId = "local.flotsam.trainer";
        private static readonly Color PanelColor = new Color32(239, 247, 243, 255);
        private static readonly Color TitleColor = new Color32(151, 213, 200, 255);
        private static readonly Color FieldColor = new Color32(253, 250, 231, 255);
        private static readonly Color ButtonColor = new Color32(182, 223, 214, 255);
        private static readonly Color AccentColor = new Color32(21, 113, 91, 255);
        private static readonly Color Ink = new Color32(38, 62, 70, 255);
        private static readonly Color WarningColor = new Color32(244, 188, 141, 255);
        private static TrainerPanel instance;
        private readonly List<Toggle> toggles = new List<Toggle>();
        private readonly CapacityOverrides warehouseCapacity = new CapacityOverrides();
        private readonly CapacityOverrides cargoCapacity = new CapacityOverrides();
        private GameObject panel;
        private Font font;
        private Text status;
        private InputField researchPoints;
        private Community mealsTown;
        private Community capacityTown;
        private bool restoreBuildFlag;
        private bool freeBuildActive;
        private bool expandedWarehouses;
        private bool expandedCargo;
        private bool autoSalvage;
        private float nextSalvage;
        private GameObject detachConfirmation;
        private float nextCapacityUpdate;
        internal static int Install()
        {
            if (instance != null)
                return 0;

            var harmony = new Harmony(PatchId);
            GameObject host = null;
            try
            {
                harmony.PatchAll(typeof(TrainerPanel).Assembly);
                host = new GameObject("Flotsam Trainer", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                DontDestroyOnLoad(host);
                var menu = host.AddComponent<TrainerPanel>();
                menu.CreateMenu();
                instance = menu;
                Debug.Log("[Flotsam Trainer] Ready (F2).");
                return 0;
            }
            catch (Exception error)
            {
                harmony.UnpatchAll(PatchId);
                if (host != null)
                    Destroy(host);
                Debug.LogError("[Flotsam Trainer] " + error);
                return 24;
            }
        }

        private void CreateMenu()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "DengXian" }, 22);
            if (font == null)
                throw new InvalidOperationException("No Chinese UI font was found.");

            CreateWindowArt();
            var host = new GameObject("Menu", typeof(RectTransform));
            window = host.GetComponent<RectTransform>();
            Position(window, (RectTransform)transform, new Rect(30, 94, 620, 628));
            panel = host;
            var shadow = AddImage(window, new Rect(-10, -4, 640, 649), new Color(0.09f, 0.22f, 0.26f, 0.34f));
            UseSlicedArt(shadow, shadowArt);
            shadow.raycastTarget = false;
            UseSlicedArt(AddImage(window, new Rect(0, 0, 620, 628), PanelColor), frameArt);

            var title = AddImage(window, new Rect(8, 8, 604, 43), TitleColor);
            UseSlicedArt(title, controlArt);
            AddGrip(title, false);
            Label(window, "漂浮城镇 · 修改器", new Rect(23, 8, 460, 43), 23, Ink);
            Label(window, "F2", new Rect(527, 8, 34, 43), 16, Ink);
            AddButton(window, "×", new Rect(570, 14, 31, 31), WarningColor, HidePanel);

            var content = new GameObject("Features", typeof(RectTransform)).GetComponent<RectTransform>();
            Position(content, window, new Rect(0, 0, 620, 510));
            var ui = GameManager.Settings?.UISettings;
            var featuresTab = AddButton(window, "功能", new Rect(18, 61, 115, 33), TitleColor, ShowFeatures);
            var itemsTab = AddButton(window, "物品库存", new Rect(141, 61, 146, 33), ButtonColor, ShowItems,
                ui?.BuildableFinishedNotification?.Icon);
            featureTab = featuresTab.targetGraphic;
            itemTab = itemsTab.targetGraphic;
            AddToggle(window, "小地图", 326, 63, value => showMinimap = value, 118);
            AddToggle(window, "快捷航行", 458, 63, value => quickSailing = value, 142);
            Label(window, "小地图：按住左键航行 · 右键拖动", new Rect(300, 92, 302, 16), 14, AccentColor);
            Section(content, "人物与生存", 108);
            AddToggle(content, "人物不死", 21, 139, value => Features.Immortal = value);
            AddToggle(content, "无需吃喝", 319, 139, SetNoMeals);
            AddToggle(content, "工人不受污染", 21, 172, TownTools.SetPollutionProof);
            AddToggle(content, "工人永远开心", 319, 172, value => TownTools.HappyWorkers = value);

            Section(content, "城镇与物流", 210);
            AddToggle(content, "无需资源建造", 21, 241, SetFreeBuild);
            AddToggle(content, "无需材料制作", 319, 241, value => TownTools.FreeCrafting = value);
            AddToggle(content, "无限负重", 21, 274, Features.SetUnlimitedWeight);
            AddToggle(content, "无限电力", 319, 274, value => Features.UnlimitedPower = value);
            AddToggle(content, "仓库无限容量", 21, 307, SetWarehouses);
            AddToggle(content, "工人及船只无限载量", 319, 307, SetCargo);
            AddToggle(content, "自动打捞附近物资", 21, 340, value =>
            {
                autoSalvage = value;
                nextSalvage = 0f;
            });
            AddButton(content, "一键治疗", new Rect(319, 340, 283, 29), ButtonColor, Features.TreatAllDrifters);

            Section(content, "倍率与点数", 379);
            AddMultiplier(content, "角色经验", 409, value => Features.FastLevel = value,
                () => Features.ExperienceMultiplier, value => Features.ExperienceMultiplier = value);
            AddMultiplier(content, "研究速度", 443, value => Features.FastResearch = value,
                () => Features.ResearchMultiplier, value => Features.ResearchMultiplier = value);
            AddMultiplier(content, "工人移速", 477, value =>
            {
                TownTools.FastWorkers = value;
                TownTools.RefreshWorkerSpeed();
            }, () => TownTools.WorkerMultiplier, value =>
            {
                TownTools.WorkerMultiplier = value;
                if (TownTools.FastWorkers)
                    TownTools.RefreshWorkerSpeed();
            }, maximum: 10f);
            AddMultiplier(content, "船只移速", 511, value => TownTools.FastBoats = value,
                () => TownTools.BoatMultiplier, value => TownTools.BoatMultiplier = value, maximum: 10f);
            Label(content, "科研点数", new Rect(21, 545, 103, 29), 18, Ink);
            researchPoints = AddInput(content, new Rect(123, 545, 95, 29), InputField.ContentType.IntegerNumber, 10);
            AddButton(content, "设置", new Rect(224, 545, 64, 29), ButtonColor, ApplyResearch);
            AddButton(content, "全员属性拉满", new Rect(319, 545, 283, 29), ButtonColor, Features.MaxAttributes);

            CreateItemPage(window, content);
            status = Label(content, "", new Rect(18, 587, 425, 28), 15, AccentColor);
            AddButton(window, "取消注入", new Rect(459, 587, 116, 28), WarningColor,
                () => detachConfirmation.SetActive(true));
            var corner = AddImage(window, new Rect(582, 589, 27, 27), new Color(0, 0, 0, 0));
            Label(window, "◢", new Rect(582, 589, 27, 27), 19, Ink, TextAnchor.MiddleCenter);
            AddGrip(corner, true);
            CreateDetachConfirmation();
            CreateMinimap();
            panel.SetActive(false);
        }

        private void CreateDetachConfirmation()
        {
            var overlay = AddImage(window, new Rect(0, 0, 620, 628), new Color(0.10f, 0.20f, 0.23f, 0.43f));
            UseSlicedArt(overlay, frameArt);
            detachConfirmation = overlay.gameObject;
            var card = AddImage(overlay.rectTransform, new Rect(109, 235, 402, 158), PanelColor);
            UseSlicedArt(card, frameArt);
            var rect = card.rectTransform;
            Label(rect, "取消注入？", new Rect(22, 11, 357, 40), 23, Ink);
            Label(rect, "将关闭功能并移除面板。", new Rect(22, 54, 357, 32), 18, Ink);
            AddButton(rect, "返回", new Rect(22, 103, 168, 34), ButtonColor,
                () => detachConfirmation.SetActive(false));
            AddButton(rect, "确认取消注入", new Rect(212, 103, 168, 34), WarningColor, Detach);
            detachConfirmation.SetActive(false);
        }

        private void Detach()
        {
            try
            {
                DisableAll();
                ReleaseBackgroundMap();
                new Harmony(PatchId).UnpatchAll(PatchId);
            }
            catch (Exception error)
            {
                Debug.LogError("[Flotsam Trainer] Detach failed: " + error);
                return;
            }

            gameObject.SetActive(false);
            Destroy(gameObject);
            instance = null;
            Debug.Log("[Flotsam Trainer] Detached.");
        }

        private void Update()
        {
            KeepWindowOnScreen();
            UpdateNavigation();
            if (Input.GetKeyDown(KeyCode.F2))
            {
                if (panel.activeSelf)
                    HidePanel();
                else
                {
                    panel.SetActive(true);
                    if (itemPage.activeSelf)
                        RefreshItemPage();
                    else
                        UpdateResearchInput();
                }
            }
            if ((expandedWarehouses || expandedCargo) && Time.unscaledTime >= nextCapacityUpdate)
                RefreshCapacities();

            var town = Community.PlayerCommunity;
            if (autoSalvage && town != null && !LoadingScreen.IsLoading && !GameManager.Gamepaused &&
                Time.unscaledTime >= nextSalvage)
            {
                nextSalvage = Time.unscaledTime + 2f;
                int count = TownTools.SalvageNearby(100);
                if (count > 0)
                    status.text = $"自动打捞 {count} 件物资";
            }
            if (Features.NoMeals && town != mealsTown)
            {
                mealsTown = town;
                ClearTownMeals(town);
            }
        }

        private void OnDestroy()
        {
            ReleaseBackgroundMap();
            if (minimapTexture != null)
            {
                minimapTexture.Release();
                Destroy(minimapTexture);
            }
            warehouseCapacity.Restore();
            cargoCapacity.Restore();
            if (freeBuildActive)
                Features.SetFreeBuild(restoreBuildFlag);
            foreach (var art in windowArt)
                Destroy(art);
            if (font != null)
                Destroy(font);
        }

        private void SetFreeBuild(bool active)
        {
            if (active)
                restoreBuildFlag = BuildingDevTools.InstantBuild;
            freeBuildActive = active;
            Features.SetFreeBuild(active || restoreBuildFlag);
        }

        private void SetNoMeals(bool active)
        {
            Features.NoMeals = active;
            mealsTown = Community.PlayerCommunity;
            if (active)
                ClearTownMeals(mealsTown);
        }

        private static void ClearTownMeals(Community town)
        {
            if (town == null)
                return;

            foreach (var agent in town.Agents)
            {
                if (agent != null && agent.IsAlive && agent.Vitals != null &&
                    agent.Vitals.Hunger != null && agent.Vitals.Thirst != null)
                    Features.ClearMeals(agent.Vitals);
            }
        }

        private void Section(RectTransform rect, string caption, int top)
        {
            UseSlicedArt(AddImage(rect, new Rect(18, top, 584, 23), TitleColor), controlArt);
            Label(rect, caption, new Rect(25, top, 245, 23), 16, Ink);
        }

        private void HidePanel()
        {
            detachConfirmation.SetActive(false);
            panel.SetActive(false);
        }

        private void UpdateResearchInput()
        {
            var research = Community.PlayerCommunity?.Research;
            researchPoints.SetTextWithoutNotify(research?.ResearchPoints.ToString(CultureInfo.InvariantCulture) ?? "");
        }

        private void ApplyResearch()
        {
            if (Community.PlayerCommunity?.Research == null || LoadingScreen.IsLoading)
            {
                status.text = "请先进入城镇";
                return;
            }
            if (!int.TryParse(researchPoints.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var points) ||
                points < 0 || points > 9999999)
            {
                status.text = "请输入 0–9999999 的整数";
                return;
            }

            TownTools.SetResearchPoints(points);
            UpdateResearchInput();
            status.text = "科研点数已设置";
        }

        private void SetWarehouses(bool enabled)
        {
            expandedWarehouses = enabled;
            if (enabled)
                RefreshCapacities();
            else
                warehouseCapacity.Restore();
        }

        private void SetCargo(bool enabled)
        {
            expandedCargo = enabled;
            if (enabled)
                RefreshCapacities();
            else
                cargoCapacity.Restore();
        }

        private void RefreshCapacities()
        {
            nextCapacityUpdate = Time.unscaledTime + 0.5f;
            var town = Community.PlayerCommunity;
            if (capacityTown != town)
            {
                warehouseCapacity.Restore();
                cargoCapacity.Restore();
                capacityTown = town;
            }
            if (town == null || LoadingScreen.IsLoading)
                return;

            if (expandedWarehouses)
            {
                foreach (var storage in town.Storages)
                {
                    if (storage == null || storage.Buildable.BuildPhase != BuildPhase.Finished)
                        continue;
                    warehouseCapacity.Expand(storage.Inventory, SubInventoryType.Storage);
                    warehouseCapacity.Expand(storage.Inventory, SubInventoryType.Liquid);
                }
            }
            if (expandedCargo)
            {
                foreach (var agent in town.Agents)
                    if (agent != null && agent.IsAlive)
                        cargoCapacity.Expand(agent.Inventory, SubInventoryType.Storage);

                foreach (var boat in town.ReturnAllBoats())
                    if (boat != null && (boat.Type == BoatType.SalvagingBoat || boat.Type == BoatType.FishingBoat))
                        cargoCapacity.Expand(boat.Buildable.Inventory, SubInventoryType.Storage);
            }
        }

        private void DisableAll()
        {
            foreach (var toggle in toggles)
                toggle.isOn = false;
        }

        private void AddToggle(RectTransform parent, string caption, int x, int y, Action<bool> change,
            int width = 282)
        {
            var row = new GameObject(caption, typeof(RectTransform), typeof(Toggle));
            var rect = row.GetComponent<RectTransform>();
            Position(rect, parent, new Rect(x, y, width, 29));
            var background = AddImage(rect, new Rect(0, 2, 25, 25), FieldColor);
            UseSlicedArt(background, controlArt);
            var mark = Label(rect, "✓", new Rect(0, 0, 25, 29), 22, AccentColor, TextAnchor.MiddleCenter);
            var label = Label(rect, caption, new Rect(34, 0, width - 34, 29), 19, Ink);
            label.raycastTarget = true;

            var toggle = row.GetComponent<Toggle>();
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.targetGraphic = background;
            toggle.graphic = mark;
            toggle.isOn = false;
            toggle.onValueChanged.AddListener(value => change(value));
            toggles.Add(toggle);
        }

        private void AddMultiplier(RectTransform parent, string caption, int y, Action<bool> change,
            Func<float> read, Action<float> write, float maximum = 1000f)
        {
            AddToggle(parent, caption, 18, y, change);
            var input = AddInput(parent, new Rect(319, y, 159, 29), InputField.ContentType.DecimalNumber, 8);
            void Apply(float value)
            {
                value = (float)Math.Round(Mathf.Clamp(value, 0.1f, maximum), 3);
                write(value);
                input.SetTextWithoutNotify(value.ToString("0.000", CultureInfo.InvariantCulture));
            }

            Apply(read());
            input.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                    !float.IsNaN(value) && !float.IsInfinity(value))
                    Apply(value);
                else
                    Apply(read());
            });
            AddButton(parent, "−", new Rect(491, y, 34, 29), ButtonColor, () => Apply(read() - 0.5f));
            AddButton(parent, "+", new Rect(536, y, 34, 29), ButtonColor, () => Apply(read() + 0.5f));
            Label(parent, "倍", new Rect(582, y, 24, 29), 19, Ink);
        }

        private InputField AddInput(RectTransform parent, Rect bounds, InputField.ContentType type, int length)
        {
            var background = AddImage(parent, bounds, FieldColor);
            UseSlicedArt(background, controlArt);
            var input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            input.contentType = type;
            input.characterLimit = length;
            input.textComponent = Label(background.rectTransform, "",
                new Rect(9, 0, bounds.width - 18, bounds.height), 19, Ink);
            return input;
        }

        private Button AddButton(RectTransform parent, string caption, Rect bounds, Color color, Action action,
            Sprite icon = null)
        {
            var image = AddImage(parent, bounds, color);
            UseSlicedArt(image, controlArt);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => action());
            float inset = icon == null ? 0 : 25;
            if (icon != null)
                AddSprite(image.rectTransform, new Rect(5, 3, 21, bounds.height - 6), icon);
            Label(image.rectTransform, caption, new Rect(inset, 0, bounds.width - inset, bounds.height), 18,
                Ink, TextAnchor.MiddleCenter);
            return button;
        }

        private static Image AddSprite(RectTransform parent, Rect bounds, Sprite sprite)
        {
            var image = AddImage(parent, bounds, Color.white);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = sprite != null;
            return image;
        }

        private static Image AddImage(RectTransform parent, Rect bounds, Color color)
        {
            var image = new GameObject("Background", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            Position(image.rectTransform, parent, bounds);
            image.color = color;
            return image;
        }

        private Text Label(RectTransform parent, string caption, Rect bounds, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            Position(label.rectTransform, parent, bounds);
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.text = caption;
            return label;
        }

        private static void Position(RectTransform rect, RectTransform parent, Rect bounds)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(bounds.x, -bounds.y);
            rect.sizeDelta = bounds.size;
        }
    }
}
