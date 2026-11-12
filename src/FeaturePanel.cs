using UnityEngine;
using UnityEngine.UI;

namespace FlotsamTrainer
{
    public sealed partial class TrainerPanel
    {
        private bool autoLandmarkSalvage;
        private float nextLandmarkSalvage;

        private void CreateFeatureControls(RectTransform content)
        {
            var options = new GameObject("Common options", typeof(RectTransform)).GetComponent<RectTransform>();
            Position(options, content, new Rect(0, 0, 620, 575));
            var multipliers = new GameObject("Multipliers", typeof(RectTransform)).GetComponent<RectTransform>();
            Position(multipliers, content, new Rect(0, 0, 620, 575));
            Graphic optionsTab = null;
            Graphic multipliersTab = null;
            optionsTab = AddButton(content, "常用开关", new Rect(18, 113, 283, 30), TitleColor, () =>
            {
                options.gameObject.SetActive(true);
                multipliers.gameObject.SetActive(false);
                optionsTab.color = TitleColor;
                multipliersTab.color = ButtonColor;
            }).targetGraphic;
            multipliersTab = AddButton(content, "倍率参数", new Rect(319, 113, 283, 30), ButtonColor, () =>
            {
                options.gameObject.SetActive(false);
                multipliers.gameObject.SetActive(true);
                optionsTab.color = ButtonColor;
                multipliersTab.color = TitleColor;
                UpdateResearchInput();
            }).targetGraphic;

            Section(options, "人物与生存", 150);
            AddToggle(options, "人物不死", 21, 181, value => Features.Immortal = value);
            AddToggle(options, "无需吃喝", 319, 181, SetNoMeals);
            AddToggle(options, "工人不受污染", 21, 214, TownTools.SetPollutionProof);
            AddToggle(options, "工人永远开心", 319, 214, value => TownTools.HappyWorkers = value);
            Section(options, "城镇与搜刮", 246);
            AddToggle(options, "无需资源建造", 21, 275, SetFreeBuild);
            AddToggle(options, "无需材料制作", 319, 275, value => TownTools.FreeCrafting = value);
            AddToggle(options, "无限负重", 21, 308, Features.SetUnlimitedWeight);
            AddToggle(options, "无限电力", 319, 308, value => Features.UnlimitedPower = value);
            AddToggle(options, "仓库无限容量", 21, 341, SetWarehouses);
            AddToggle(options, "工人及船只无限载量", 319, 341, SetCargo);
            AddToggle(options, "自动打捞漂浮物资", 21, 374, value =>
            {
                autoSalvage = value;
                nextSalvage = 0f;
            });
            AddToggle(options, "自动搜刮附近建筑", 319, 374, value =>
            {
                autoLandmarkSalvage = value;
                nextLandmarkSalvage = 0f;
            });
            AddToggle(options, "搜刮无需工具", 21, 407, value =>
            {
                AcquisitionTools.NoTools = value;
                nextLandmarkSalvage = 0f;
            });
            Label(options, "自动收取范围内建筑资源，遵守筛选和仓位。",
                new Rect(21, 446, 574, 25), 16, AccentColor);
            Label(options, "无需工具会解锁附近搜刮类别，解锁随游戏保存。",
                new Rect(21, 473, 574, 25), 16, AccentColor);
            AddButton(options, "一键治疗", new Rect(21, 521, 280, 33), ButtonColor, Features.TreatAllDrifters);
            AddButton(options, "全员属性拉满", new Rect(319, 521, 283, 33), ButtonColor, Features.MaxAttributes);

            Section(multipliers, "独立开关 · 输入后回车应用", 150);
            AddMultiplier(multipliers, "角色经验", 180, value => Features.FastLevel = value,
                () => Features.ExperienceMultiplier, value => Features.ExperienceMultiplier = value);
            AddMultiplier(multipliers, "研究速度", 214, value => Features.FastResearch = value,
                () => Features.ResearchMultiplier, value => Features.ResearchMultiplier = value);
            AddMultiplier(multipliers, "制作速度", 248, value => TownTools.FastCrafting = value,
                () => TownTools.CraftMultiplier, value => TownTools.CraftMultiplier = value, maximum: 100f);
            AddMultiplier(multipliers, "建造速度", 282, value => AcquisitionTools.FastConstruction = value,
                () => AcquisitionTools.ConstructionMultiplier, value => AcquisitionTools.ConstructionMultiplier = value, maximum: 100f);
            AddMultiplier(multipliers, "拆除速度", 316, value => AcquisitionTools.FastDemolition = value,
                () => AcquisitionTools.DemolitionMultiplier, value => AcquisitionTools.DemolitionMultiplier = value, maximum: 100f);
            AddMultiplier(multipliers, "物品获取", 350, value => AcquisitionTools.MultiplyItems = value,
                () => AcquisitionTools.ItemMultiplier, value => AcquisitionTools.ItemMultiplier = value, maximum: 100f, minimum: 1f);
            AddMultiplier(multipliers, "工人移速", 384, value =>
            {
                TownTools.FastWorkers = value;
                TownTools.RefreshWorkerSpeed();
            }, () => TownTools.WorkerMultiplier, value =>
            {
                TownTools.WorkerMultiplier = value;
                if (TownTools.FastWorkers)
                    TownTools.RefreshWorkerSpeed();
            }, maximum: 10f);
            AddMultiplier(multipliers, "打捞船移速", 418, value => TownTools.FastSalvageBoats = value,
                () => TownTools.SalvageBoatMultiplier, value => TownTools.SalvageBoatMultiplier = value, maximum: 10f);
            AddMultiplier(multipliers, "渔船移速", 452, value => TownTools.FastFishingBoats = value,
                () => TownTools.FishingBoatMultiplier, value => TownTools.FishingBoatMultiplier = value, maximum: 10f);
            AddMultiplier(multipliers, "主船航速", 486, value => TownTools.FastTownShip = value,
                () => TownTools.TownShipMultiplier, value => TownTools.TownShipMultiplier = value, maximum: 10f);
            Label(multipliers, "科研点数", new Rect(21, 537, 103, 29), 18, Ink);
            researchPoints = AddInput(multipliers, new Rect(123, 537, 200, 29), InputField.ContentType.IntegerNumber, 10);
            AddButton(multipliers, "设置", new Rect(337, 537, 96, 29), ButtonColor, ApplyResearch);
            multipliers.gameObject.SetActive(false);
        }

        private void UpdateLandmarkSalvage()
        {
            if ((!autoLandmarkSalvage && !AcquisitionTools.NoTools) || LoadingScreen.IsLoading ||
                Community.PlayerCommunity == null || GameManager.Gamepaused || Time.unscaledTime < nextLandmarkSalvage)
                return;
            nextLandmarkSalvage = Time.unscaledTime + 2f;
            int count = AcquisitionTools.VisitNearbyLandmarks(autoLandmarkSalvage, 100);
            if (count > 0)
                status.text = $"建筑搜刮 {count} 件物资";
        }
    }
}
