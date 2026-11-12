using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace FlotsamTrainer
{
    public sealed partial class TrainerPanel
    {
        private const int TileColumns = 6;
        private const int ItemsPerPage = 18;
        private readonly List<ItemProperties> matchingItems = new List<ItemProperties>();
        private readonly Button[] itemButtons = new Button[ItemsPerPage];
        private readonly Image[] itemBackgrounds = new Image[ItemsPerPage];
        private Graphic featureTab;
        private Graphic itemTab;
        private readonly Image[] itemIcons = new Image[ItemsPerPage];
        private readonly Text[] itemNames = new Text[ItemsPerPage];
        private readonly Text[] itemCounts = new Text[ItemsPerPage];
        private GameObject featurePage;
        private GameObject itemPage;
        private InputField itemSearch;
        private InputField itemQuantity;
        private Text itemPageNumber;
        private Text selectedItemName;
        private Text itemStatus;
        private Button applyItem;
        private ItemProperties selectedItem;
        private Community itemTown;
        private int itemPageIndex;

        private void CreateItemPage(RectTransform frame, RectTransform features)
        {
            featurePage = features.gameObject;
            var rect = new GameObject("Item Inventory", typeof(RectTransform)).GetComponent<RectTransform>();
            Position(rect, frame, new Rect(0, 0, 620, 520));
            itemPage = rect.gameObject;
            itemSearch = AddInput(rect, new Rect(18, 107, 399, 30), InputField.ContentType.Standard, 48);
            itemSearch.placeholder = Label((RectTransform)itemSearch.transform, "搜索物品名称",
                new Rect(9, 0, 380, 30), 17, new Color32(99, 117, 116, 255));
            itemSearch.onValueChanged.AddListener(_ =>
            {
                itemPageIndex = 0;
                RefreshItemPage();
            });


            AddButton(rect, "‹", new Rect(441, 108, 33, 27), ButtonColor, () => ChangeItemPage(-1));
            AddButton(rect, "›", new Rect(569, 108, 33, 27), ButtonColor, () => ChangeItemPage(1));
            itemPageNumber = Label(rect, "", new Rect(478, 108, 86, 27), 16, Ink, TextAnchor.MiddleCenter);

            for (int i = 0; i < ItemsPerPage; i++)
            {
                int row = i;
                var tile = new Rect(18 + i % TileColumns * 99, 150 + i / TileColumns * 99, 89, 91);
                var button = AddButton(rect, "", tile, PanelColor, () => SelectItem(row));
                itemButtons[i] = button;
                var rowRect = (RectTransform)button.transform;
                itemBackgrounds[i] = AddImage(rowRect, new Rect(16, 6, 57, 57), TitleColor);
                UseSlicedArt(itemBackgrounds[i], controlArt);
                itemBackgrounds[i].raycastTarget = false;
                itemIcons[i] = AddSprite(rowRect, new Rect(22, 10, 45, 45), null);
                itemNames[i] = Label(rowRect, "", new Rect(4, 66, 81, 22), 14, Ink, TextAnchor.MiddleCenter);
                itemNames[i].resizeTextForBestFit = true;
                itemNames[i].resizeTextMinSize = 10;
                itemNames[i].resizeTextMaxSize = 14;
                itemCounts[i] = Label(rowRect, "", new Rect(17, 43, 56, 21), 16, Color.white, TextAnchor.MiddleRight);
                itemCounts[i].fontStyle = FontStyle.Bold;
                var outline = itemCounts[i].gameObject.AddComponent<Outline>();
                outline.effectColor = Ink;
                outline.effectDistance = new Vector2(1.25f, -1.25f);
            }

            selectedItemName = Label(rect, "", new Rect(18, 454, 307, 32), 18, Ink);
            itemQuantity = AddInput(rect, new Rect(331, 454, 129, 32), InputField.ContentType.IntegerNumber, 10);
            applyItem = AddButton(rect, "设置数量", new Rect(473, 454, 129, 32), ButtonColor, ApplyItemQuantity);
            itemStatus = Label(rect, "", new Rect(18, 587, 425, 28), 15, AccentColor);
            itemPage.SetActive(false);
        }

        private void ShowFeatures()
        {
            itemPage.SetActive(false);
            drifterPage.SetActive(false);
            featurePage.SetActive(true);
            featureTab.color = TitleColor;
            itemTab.color = ButtonColor;
            drifterTab.color = ButtonColor;
            UpdateResearchInput();
        }

        private void ShowItems()
        {
            featurePage.SetActive(false);
            drifterPage.SetActive(false);
            itemPage.SetActive(true);
            featureTab.color = ButtonColor;
            itemTab.color = TitleColor;
            drifterTab.color = ButtonColor;
            RefreshItemPage();
        }
        private void RefreshItemPage()
        {
            var town = Community.PlayerCommunity;
            if (itemTown != town)
            {
                selectedItem = null;
                itemTown = town;
                itemStatus.text = "";
            }

            matchingItems.Clear();
            var catalog = GameManager.Settings?.ItemSettings?.ItemProperties;
            if (catalog != null)
            {
                string query = itemSearch.text.Trim();
                foreach (var item in catalog)
                {
                    if (item != null && item.Id >= 0 && item.InventorySprite != null &&
                        !item.IsQuestItem && !item.IsSuperItem &&
                        (ItemName(item).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         item.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                        matchingItems.Add(item);
                }
            }

            int pages = Math.Max(1, (matchingItems.Count + ItemsPerPage - 1) / ItemsPerPage);
            itemPageIndex = Mathf.Clamp(itemPageIndex, 0, pages - 1);
            itemPageNumber.text = $"{itemPageIndex + 1} / {pages}";
            RefreshItemRows();
            RefreshSelectedItem();
        }

        private void ChangeItemPage(int direction)
        {
            itemPageIndex += direction;
            RefreshItemPage();
        }

        private void SelectItem(int row)
        {
            selectedItem = matchingItems[itemPageIndex * ItemsPerPage + row];
            itemStatus.text = "";
            RefreshItemRows();
            RefreshSelectedItem();
        }

        private void RefreshItemRows()
        {
            for (int row = 0; row < ItemsPerPage; row++)
            {
                int index = itemPageIndex * ItemsPerPage + row;
                bool visible = index < matchingItems.Count;
                itemButtons[row].gameObject.SetActive(visible);
                if (!visible)
                    continue;

                var item = matchingItems[index];
                itemButtons[row].targetGraphic.color = item == selectedItem ? TitleColor : PanelColor;
                itemBackgrounds[row].color = item.ItemType != null ? item.ItemType.Color : TitleColor;
                itemIcons[row].sprite = item.InventorySprite;
                itemIcons[row].enabled = true;
                itemNames[row].text = ItemName(item);
                itemCounts[row].text = itemTown == null ? "—" :
                    TownTools.CountStoredItems(item).ToString(CultureInfo.InvariantCulture);
            }
        }

        private void RefreshSelectedItem()
        {
            bool ready = itemTown != null && selectedItem != null && !LoadingScreen.IsLoading;
            applyItem.interactable = itemQuantity.interactable = ready;
            selectedItemName.text = selectedItem == null ? "请选择物品" : ItemName(selectedItem);
            itemQuantity.SetTextWithoutNotify(ready
                ? TownTools.CountStoredItems(selectedItem).ToString(CultureInfo.InvariantCulture)
                : "");
            if (itemTown == null)
                itemStatus.text = "进入城镇后可设置库存";
        }

        private static string ItemName(ItemProperties item)
        {
            return string.IsNullOrWhiteSpace(item.LocalizedName) ? item.name : item.LocalizedName.Replace('\n', ' ');
        }

        private void ApplyItemQuantity()
        {
            if (itemTown != Community.PlayerCommunity || itemTown == null || selectedItem == null ||
                LoadingScreen.IsLoading)
            {
                RefreshItemPage();
                return;
            }
            if (!int.TryParse(itemQuantity.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) ||
                quantity < 0 || quantity > 99999)
            {
                itemStatus.text = "请输入 0–99999 的整数";
                return;
            }

            RefreshCapacities();
            int result = TownTools.SetStoredItems(selectedItem, quantity);
            RefreshItemRows();
            RefreshSelectedItem();
            itemStatus.text = result == quantity ? $"已设置为 {result}" : $"当前 {result}，受仓位、筛选或预留限制";
        }
    }
}
