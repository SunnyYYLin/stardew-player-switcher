using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewPlayerSwitcher.Models;
using StardewPlayerSwitcher.Services;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace StardewPlayerSwitcher.UI;

internal sealed class CoopHostPickerMenu : IClickableMenu
{
    private const int PreferredMenuWidth = 1040;
    private const int PreferredMenuHeight = 680;
    private const int MinimumMenuWidth = 840;
    private const int MinimumMenuHeight = 560;
    private const int OuterMargin = 24;
    private const int OuterPadding = 28;
    private const int SectionGap = 14;
    private const int FooterHeight = 124;
    private const int CandidateHeaderHeight = 58;
    private const int CandidateInset = 22;
    private const int RowHeight = 86;
    private const int RowGap = 10;
    private const int ButtonHeight = 56;
    private const int MinimumConfirmButtonWidth = 180;
    private const int MaximumConfirmButtonWidth = 236;
    private const int MinimumCancelButtonWidth = 108;
    private const int MaximumCancelButtonWidth = 156;
    private const int ArrowButtonWidth = 60;
    private const int ArrowButtonHeight = 42;

    private readonly SaveSlotSummary save;
    private readonly SaveSwapService saveSwapService;
    private readonly ITranslationHelper i18n;
    private readonly Action<HostCandidate> onConfirm;
    private readonly Action onCancel;
    private readonly bool useRegularFont;

    private readonly List<ClickableComponent> candidateComponents = new();

    private ClickableComponent confirmButton = null!;
    private ClickableComponent cancelButton = null!;
    private ClickableComponent upButton = null!;
    private ClickableComponent downButton = null!;

    private Rectangle headerBounds;
    private Rectangle candidatePanelBounds;
    private Rectangle footerBounds;
    private Rectangle statusTextBounds;
    private int candidateScrollIndex;
    private int visibleCandidateCount;
    private HostCandidate selectedCandidate;
    private string statusMessage;
    private Color statusColor = Game1.textColor;
    private bool isWorking;

    public CoopHostPickerMenu(
        SaveSlotSummary save,
        SaveSwapService saveSwapService,
        ITranslationHelper i18n,
        Action<HostCandidate> onConfirm,
        Action onCancel)
        : base(
            0,
            0,
            0,
            0,
            showUpperRightCloseButton: false)
    {
        this.save = save;
        this.saveSwapService = saveSwapService;
        this.i18n = i18n;
        this.onConfirm = onConfirm;
        this.onCancel = onCancel;
        this.useRegularFont = this.i18n.Locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        this.selectedCandidate = save.Candidates.FirstOrDefault(candidate => candidate.IsCurrentHost) ?? save.Candidates[0];
        this.statusMessage = string.Empty;
        this.UpdateSelectionStatus();

        this.initializeUpperRightCloseButton();
        this.RefreshLayout();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.RefreshLayout();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.isWorking)
            return;

        if (this.upperRightCloseButton.containsPoint(x, y) || this.cancelButton.containsPoint(x, y))
        {
            Game1.playSound("bigDeSelect");
            this.onCancel();
            return;
        }

        if (this.confirmButton.containsPoint(x, y))
        {
            this.ConfirmSelection();
            return;
        }

        bool showScrollButtons = this.save.Candidates.Count > this.visibleCandidateCount;

        if (showScrollButtons && this.upButton.containsPoint(x, y))
        {
            this.ScrollCandidates(-1);
            return;
        }

        if (showScrollButtons && this.downButton.containsPoint(x, y))
        {
            this.ScrollCandidates(1);
            return;
        }

        for (int index = 0; index < this.candidateComponents.Count; index++)
        {
            ClickableComponent component = this.candidateComponents[index];
            if (!component.containsPoint(x, y))
                continue;

            int candidateIndex = this.candidateScrollIndex + index;
            if (candidateIndex >= this.save.Candidates.Count)
                continue;

            this.selectedCandidate = this.save.Candidates[candidateIndex];
            this.UpdateSelectionStatus();
            Game1.playSound("smallSelect");
            return;
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        Point mousePoint = new(Game1.getMouseX(), Game1.getMouseY());
        if (this.candidatePanelBounds.Contains(mousePoint))
            this.ScrollCandidates(direction > 0 ? -1 : 1);
    }

    public override void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys key)
    {
        if (this.isWorking)
            return;

        if (Game1.options.doesInputListContain(Game1.options.menuButton, key) || key == Microsoft.Xna.Framework.Input.Keys.Escape)
        {
            Game1.playSound("bigDeSelect");
            this.onCancel();
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void performHoverAction(int x, int y)
    {
        this.upperRightCloseButton.tryHover(x, y);
    }

    public override void draw(SpriteBatch b)
    {
        this.drawBackground(b);
        IClickableMenu.drawTextureBox(b, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);
        this.upperRightCloseButton.draw(b);

        this.DrawHeader(b);
        this.DrawPanel(b, this.candidatePanelBounds, this.T("panel.farmers"));
        this.DrawPanel(b, this.footerBounds, string.Empty);
        this.DrawCandidates(b);
        this.DrawFooter(b);
        this.DrawButton(b, this.confirmButton, this.GetConfirmLabel(), enabled: !this.isWorking);
        this.DrawButton(b, this.cancelButton, this.T("button.back"), enabled: !this.isWorking);

        if (this.save.Candidates.Count > this.visibleCandidateCount)
        {
            this.DrawArrowButton(b, this.upButton, !this.isWorking && this.candidateScrollIndex > 0, pointsUp: true);
            this.DrawArrowButton(b, this.downButton, !this.isWorking && this.candidateScrollIndex + this.visibleCandidateCount < this.save.Candidates.Count, pointsUp: false);
        }

        this.drawMouse(b);
    }

    private void RefreshLayout()
    {
        this.width = ResolveMenuDimension(Game1.viewport.Width, PreferredMenuWidth, MinimumMenuWidth);
        this.height = ResolveMenuDimension(Game1.viewport.Height, PreferredMenuHeight, MinimumMenuHeight);
        this.xPositionOnScreen = (Game1.viewport.Width - this.width) / 2;
        this.yPositionOnScreen = (Game1.viewport.Height - this.height) / 2;

        this.upperRightCloseButton.bounds = new Rectangle(this.xPositionOnScreen + this.width - 80, this.yPositionOnScreen + 12, 64, 64);

        int pixelLineHeight = this.GetLineHeight();
        int contentLeft = this.xPositionOnScreen + OuterPadding;
        int contentTop = this.yPositionOnScreen + OuterPadding;
        int contentWidth = this.width - (OuterPadding * 2);
        int headerHeight = (pixelLineHeight * 3) + 12;
        int footerY = this.yPositionOnScreen + this.height - OuterPadding - FooterHeight;

        this.headerBounds = new Rectangle(contentLeft, contentTop, contentWidth, headerHeight);
        this.footerBounds = new Rectangle(contentLeft, footerY, contentWidth, FooterHeight);

        int candidateY = this.headerBounds.Bottom + SectionGap;
        int candidateHeight = Math.Max(RowHeight + CandidateHeaderHeight + CandidateInset, this.footerBounds.Y - SectionGap - candidateY);
        this.candidatePanelBounds = new Rectangle(contentLeft, candidateY, contentWidth, candidateHeight);
        this.visibleCandidateCount = this.GetVisibleCandidateCount();
        this.candidateScrollIndex = Math.Clamp(this.candidateScrollIndex, 0, Math.Max(0, this.save.Candidates.Count - this.visibleCandidateCount));

        int buttonY = this.footerBounds.Y + ((this.footerBounds.Height - ButtonHeight) / 2);
        int confirmButtonWidth = this.GetConfirmButtonWidth();
        int cancelButtonWidth = this.GetCancelButtonWidth();
        this.confirmButton = new ClickableComponent(new Rectangle(this.footerBounds.Right - confirmButtonWidth - 18, buttonY, confirmButtonWidth, ButtonHeight), "Confirm");
        this.cancelButton = new ClickableComponent(new Rectangle(this.footerBounds.X + 18, buttonY, cancelButtonWidth, ButtonHeight), "Cancel");
        this.statusTextBounds = new Rectangle(
            this.cancelButton.bounds.Right + 20,
            this.footerBounds.Y + 24,
            Math.Max(120, this.confirmButton.bounds.X - this.cancelButton.bounds.Right - 40),
            this.footerBounds.Height - 48);

        int arrowY = this.candidatePanelBounds.Y + 12;
        this.upButton = new ClickableComponent(new Rectangle(this.candidatePanelBounds.Right - 140, arrowY, ArrowButtonWidth, ArrowButtonHeight), "Up");
        this.downButton = new ClickableComponent(new Rectangle(this.candidatePanelBounds.Right - 72, arrowY, ArrowButtonWidth, ArrowButtonHeight), "Down");

        this.RefreshClickableComponents();
    }

    private void RefreshClickableComponents()
    {
        this.candidateComponents.Clear();

        int candidateListTop = this.candidatePanelBounds.Y + CandidateHeaderHeight;
        int candidateWidth = this.candidatePanelBounds.Width - (CandidateInset * 2);
        int visibleCount = Math.Min(this.visibleCandidateCount, this.save.Candidates.Count - this.candidateScrollIndex);
        for (int index = 0; index < visibleCount; index++)
        {
            Rectangle bounds = new(
                this.candidatePanelBounds.X + CandidateInset,
                candidateListTop + index * (RowHeight + RowGap),
                candidateWidth,
                RowHeight);
            this.candidateComponents.Add(new ClickableComponent(bounds, $"Candidate_{index}"));
        }
    }

    private void DrawHeader(SpriteBatch b)
    {
        int x = this.headerBounds.X;
        int y = this.headerBounds.Y;
        int maxWidth = this.headerBounds.Width - 12;
        int lineHeight = this.GetLineHeight();

        this.DrawUiText(b, this.T("menu.title"), x, y, maxWidth, Game1.textColor);
        y += lineHeight + 2;
        this.DrawValueLine(b, this.T("menu.save"), this.save.FarmName, x, y, maxWidth);
        y += lineHeight - 2;
        this.DrawValueLine(b, this.T("menu.host"), this.save.CurrentHostName, x, y, maxWidth);
    }

    private void DrawPanel(SpriteBatch b, Rectangle bounds, string title)
    {
        IClickableMenu.drawTextureBox(b, bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White);
        if (!string.IsNullOrWhiteSpace(title))
            this.DrawUiText(b, title, bounds.X + 18, bounds.Y + 16, bounds.Width - 36, Game1.textColor);
    }

    private void DrawCandidates(SpriteBatch b)
    {
        for (int index = 0; index < this.candidateComponents.Count; index++)
        {
            int candidateIndex = this.candidateScrollIndex + index;
            if (candidateIndex >= this.save.Candidates.Count)
                break;

            HostCandidate candidate = this.save.Candidates[candidateIndex];
            ClickableComponent component = this.candidateComponents[index];
            bool isSelected = this.selectedCandidate.UniqueMultiplayerId == candidate.UniqueMultiplayerId;

            this.DrawListItem(b, component.bounds, isSelected);

            string nameLabel = candidate.Name;
            string roleLabel = candidate.IsCurrentHost ? this.T("candidate.host") : this.T("candidate.hand");
            string idLabel = this.T("candidate.id", new { value = FormatShortId(candidate.UniqueMultiplayerId) });
            int roleWidth = this.MeasureTextWidth(roleLabel);
            int nameMaxWidth = component.bounds.Width - roleWidth - 44;
            Color roleColor = candidate.IsCurrentHost
                ? new Color(20, 92, 52)
                : new Color(120, 72, 24);

            this.DrawUiText(b, nameLabel, component.bounds.X + 14, component.bounds.Y + 10, nameMaxWidth, Game1.textColor);
            this.DrawUiText(b, roleLabel, component.bounds.Right - roleWidth - 14, component.bounds.Y + 10, roleWidth, roleColor);
            this.DrawUiText(b, idLabel, component.bounds.X + 14, component.bounds.Y + 42, component.bounds.Width - 28, new Color(80, 50, 20));
        }
    }

    private void DrawFooter(SpriteBatch b)
    {
        int x = this.statusTextBounds.X;
        int y = this.statusTextBounds.Y;
        int lineHeight = this.GetLineHeight();

        this.DrawValueLine(b, this.T("footer.selected"), this.selectedCandidate.Name, x, y, this.statusTextBounds.Width);
        y += lineHeight - 2;
        this.DrawUiText(b, this.statusMessage, x, y, this.statusTextBounds.Width, this.statusColor);
    }

    private void DrawListItem(SpriteBatch b, Rectangle bounds, bool isSelected)
    {
        IClickableMenu.drawTextureBox(b, bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White);

        if (!isSelected)
            return;

        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Height - 8), new Color(20, 92, 52) * 0.18f);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 4, bounds.Y + 4, 6, bounds.Height - 8), new Color(20, 92, 52) * 0.85f);
    }

    private void DrawButton(SpriteBatch b, ClickableComponent component, string label, bool enabled)
    {
        Color tint = enabled ? Color.White : Color.Gray;
        Color textColor = enabled ? Game1.textColor : new Color(96, 96, 96);
        IClickableMenu.drawTextureBox(b, component.bounds.X, component.bounds.Y, component.bounds.Width, component.bounds.Height, tint);
        this.DrawUiTextCentered(b, label, component.bounds, textColor);
    }

    private void DrawArrowButton(SpriteBatch b, ClickableComponent component, bool enabled, bool pointsUp)
    {
        Color tint = enabled ? Color.White : Color.Gray;
        Color color = enabled ? Game1.textColor : new Color(96, 96, 96);
        IClickableMenu.drawTextureBox(b, component.bounds.X, component.bounds.Y, component.bounds.Width, component.bounds.Height, tint);
        this.DrawArrowGlyph(b, component.bounds, color, pointsUp);
    }

    private void ConfirmSelection()
    {
        try
        {
            this.isWorking = true;
            this.statusMessage = this.selectedCandidate.IsCurrentHost
                ? this.T("status.starting")
                : this.T("status.switching");
            this.statusColor = new Color(24, 96, 48);
            Game1.playSound("newArtifact");
            this.onConfirm(this.selectedCandidate);
        }
        catch (Exception ex)
        {
            this.isWorking = false;
            this.statusMessage = this.T("status.start_failed", new { detail = this.TrimStatusDetail(ex.Message) });
            this.statusColor = new Color(128, 32, 32);
            Game1.playSound("cancel");
            this.saveSwapService.LoadSaveSlot(this.save.SaveDirectoryPath);
        }
    }

    private void ScrollCandidates(int delta)
    {
        int maxScroll = Math.Max(0, this.save.Candidates.Count - this.visibleCandidateCount);
        int newValue = Math.Clamp(this.candidateScrollIndex + delta, 0, maxScroll);
        if (newValue == this.candidateScrollIndex)
            return;

        this.candidateScrollIndex = newValue;
        Game1.playSound("shiny4");
        this.RefreshClickableComponents();
    }

    private string GetConfirmLabel()
    {
        return this.selectedCandidate.IsCurrentHost
            ? this.T("button.start")
            : this.T("button.switch");
    }

    private int GetVisibleCandidateCount()
    {
        int listHeight = this.candidatePanelBounds.Height - CandidateHeaderHeight - CandidateInset;
        return Math.Max(1, (listHeight + RowGap) / (RowHeight + RowGap));
    }

    private void UpdateSelectionStatus()
    {
        this.statusMessage = this.selectedCandidate.IsCurrentHost
            ? this.T("status.current")
            : this.T("status.switch");
        this.statusColor = this.selectedCandidate.IsCurrentHost
            ? new Color(20, 92, 52)
            : new Color(120, 72, 24);
    }

    private void DrawArrowGlyph(SpriteBatch b, Rectangle bounds, Color color, bool pointsUp)
    {
        int centerX = bounds.Center.X;
        int startY = pointsUp ? bounds.Center.Y - 10 : bounds.Center.Y + 2;

        for (int row = 0; row < 5; row++)
        {
            int width = 1 + (row * 4);
            int y = pointsUp ? startY + (row * 4) : startY - (row * 4);
            Rectangle stripe = new(centerX - (width / 2), y, width, 3);
            b.Draw(Game1.staminaRect, stripe, color);
        }
    }

    private string TrimUiText(string text, int maxWidth)
    {
        if (this.MeasureTextWidth(text) <= maxWidth)
            return text;

        const string ellipsis = "...";
        int length = text.Length;
        while (length > 1)
        {
            string candidate = text[..length] + ellipsis;
            if (this.MeasureTextWidth(candidate) <= maxWidth)
                return candidate;

            length--;
        }

        return ellipsis;
    }

    private void DrawValueLine(SpriteBatch b, string label, string value, int x, int y, int maxWidth)
    {
        this.DrawValueLine(b, label, value, x, y, maxWidth, Game1.textColor);
    }

    private void DrawValueLine(SpriteBatch b, string label, string value, int x, int y, int maxWidth, Color color)
    {
        this.DrawUiText(b, $"{label}: {value}", x, y, maxWidth, color);
    }

    private void DrawUiText(SpriteBatch b, string text, int x, int y, int maxWidth, Color color)
    {
        string trimmed = this.TrimUiText(text, maxWidth);
        if (this.useRegularFont)
        {
            Utility.drawTextWithShadow(b, trimmed, Game1.smallFont, new Vector2(x, y), color);
            return;
        }

        SpriteText.drawString(b, trimmed, x, y, color: color);
    }

    private void DrawUiTextCentered(SpriteBatch b, string text, Rectangle bounds, Color color)
    {
        string trimmed = this.TrimUiText(text, bounds.Width - 20);
        if (this.useRegularFont)
        {
            Vector2 size = Game1.smallFont.MeasureString(trimmed);
            Vector2 position = new(
                bounds.X + ((bounds.Width - size.X) / 2f),
                bounds.Y + ((bounds.Height - size.Y) / 2f));
            Utility.drawTextWithShadow(b, trimmed, Game1.smallFont, position, color);
            return;
        }

        int textY = bounds.Center.Y - (SpriteText.getHeightOfString(trimmed) / 2);
        SpriteText.drawStringHorizontallyCenteredAt(
            b,
            trimmed,
            bounds.Center.X,
            textY,
            color: color,
            maxWidth: bounds.Width - 20);
    }

    private int MeasureTextWidth(string text)
    {
        if (this.useRegularFont)
            return (int)Math.Ceiling(Game1.smallFont.MeasureString(text).X);

        return SpriteText.getWidthOfString(text);
    }

    private int GetLineHeight()
    {
        if (this.useRegularFont)
            return (int)Math.Ceiling(Game1.smallFont.MeasureString("Ty").Y);

        return SpriteText.getHeightOfString("TT");
    }

    private static int ResolveMenuDimension(int viewportSize, int preferredSize, int minimumSize)
    {
        int paddedSize = viewportSize - (OuterMargin * 2);
        if (paddedSize >= minimumSize)
            return Math.Min(preferredSize, paddedSize);

        return Math.Max(320, viewportSize - 16);
    }

    private string TrimStatusDetail(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return this.T("status.check_smapi_log");

        string singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 28
            ? singleLine
            : $"{singleLine[..25]}...";
    }

    private int GetConfirmButtonWidth()
    {
        int labelWidth = Math.Max(
            this.MeasureTextWidth(this.T("button.start")),
            this.MeasureTextWidth(this.T("button.switch")));
        int paddedWidth = labelWidth + (this.useRegularFont ? 44 : 56);
        return Math.Clamp(paddedWidth, MinimumConfirmButtonWidth, MaximumConfirmButtonWidth);
    }

    private int GetCancelButtonWidth()
    {
        int labelWidth = this.MeasureTextWidth(this.T("button.back"));
        int paddedWidth = labelWidth + (this.useRegularFont ? 36 : 48);
        return Math.Clamp(paddedWidth, MinimumCancelButtonWidth, MaximumCancelButtonWidth);
    }

    private string T(string key)
    {
        return this.i18n.Get(key).ToString();
    }

    private string T(string key, object tokens)
    {
        return this.i18n.Get(key, tokens).ToString();
    }

    private static string FormatShortId(long uniqueMultiplayerId)
    {
        string raw = uniqueMultiplayerId.ToString();
        return raw.Length <= 6
            ? raw
            : $"...{raw[^6..]}";
    }
}
