using System;
using KingdomFoundation.Api;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomGlobalLedger
{
    internal sealed class LedgerRateCell
    {
        readonly RectTransform root;
        readonly TMP_Text amount;
        readonly TMP_Text denomination;
        readonly Image coin;
        readonly string numericSpacing;

        internal LedgerRateCell(LedgerUi ui, Transform parent, string name)
        {
            root = LedgerUi.Rect(parent, name);
            amount = ui.Text(root, "Amount", "", 17);
            amount.alignment = TextAlignmentOptions.MidlineRight;
            amount.enableAutoSizing = true;
            amount.fontSizeMin = 12;
            amount.fontSizeMax = 17;
            amount.richText = true;
            float advance = 0;
            foreach (char digit in "0123456789,+-.")
                advance = Mathf.Max(advance, amount.GetPreferredValues(digit.ToString()).x / amount.fontSize);
            numericSpacing = "<mspace=" + advance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "em>";
            LedgerUi.Fill(amount.rectTransform, 0, 0, 4, 0);
            coin = LedgerUi.Rect(root, "Coin").gameObject.AddComponent<Image>();
            coin.preserveAspect = true;
            coin.raycastTarget = false;
            denomination = ui.Text(root, "Denomination", "", 14);
        }

        internal void Hide() => root.gameObject.SetActive(false);

        internal void Bind(LedgerRowData row, long value, bool net, LedgerPanel owner, float x)
        {
            bool visible = !row.IsHeader && (row.GameCurrency.HasValue || row.Item != null);
            root.gameObject.SetActive(visible);
            if (!visible) return;
            float width = owner.Mode == LedgerPanel.ViewMode.Basic ? LedgerPanel.NetX + LedgerPanel.CellWidth - LedgerPanel.IncomeX : LedgerPanel.CellWidth;
            if (owner.Mode == LedgerPanel.ViewMode.Basic) x = LedgerPanel.IncomeX;
            LedgerUi.Place(root, x, 0, width, LedgerPanel.RowHeight - 1);
            amount.color = net ? value > 0 ? LedgerUi.Gain : value < 0 ? LedgerUi.Loss : LedgerUi.Muted : LedgerUi.Paper;
            double rate = owner.Rate(value);
            string unit = "";
            bool gold = row.GameCurrency == GameCurrency.Gold;
            coin.gameObject.SetActive(gold);
            denomination.gameObject.SetActive(gold);
            if (gold)
            {
                double magnitude = Math.Abs(rate);
                double divisor = magnitude >= 10000 ? 10000 : magnitude >= 100 ? 100 : 1;
                unit = divisor == 10000 ? "g" : divisor == 100 ? "s" : "c";
                coin.sprite = divisor == 10000 ? LedgerUi.GoldCoin() : divisor == 100 ? LedgerUi.SilverCoin() : LedgerUi.CopperCoin();
                rate /= divisor;
            }
            // Use the font's own widest numeric advance: forcing a narrower cell
            // makes this pixel face overlap even though TMP reports no truncation.
            amount.text = numericSpacing + LedgerUi.FormatAmount(rate, net) + "</mspace>";
            LedgerUi.Fill(amount.rectTransform, 0, 0, gold ? 40 : 4, 0);
            if (gold)
            {
                LedgerUi.Place(coin.rectTransform, width - 36, 9, 18, 18);
                LedgerUi.Place(denomination.rectTransform, width - 16, 0, 14, LedgerPanel.RowHeight - 1);
                denomination.text = unit;
                denomination.color = amount.color;
            }
        }
    }
}
