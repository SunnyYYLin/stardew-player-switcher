using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewPlayerSwitcher.Models;
using StardewPlayerSwitcher.Services;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace StardewPlayerSwitcher.UI;

internal sealed class CoopHostPickerMenu : IClickableMenu
{
    private const int MenuWidth = 1080;
    private const int MenuHeight = 760;
    private const int RowHeight = 108;
    private const int RowGap = 12;
    private const int MaxVisibleCandidates = 3;

    private readonly SaveSlotSummary save;
    private readonly SaveSwapService saveSwapService;
    private readonly Action<HostCandidate> onConfirm;
    private readonly Action onCancel;

    private readonly List<ClickableComponent> candidateComponents = new();

    private ClickableComponent confirmButton = null!;
    private ClickableComponent cancelButton = null!;
    private ClickableComponent upButton = null!;
    private ClickableComponent downButton = null!;

    private Rectangle candidatePanelBounds;
    private Rectangle footerBounds;
    private int candidateScrollIndex;
    private HostCandidate selectedCandidate;
    private string statusMessage;
    private Color statusColor = Game1.textColor;
    private bool isWorking;

    public CoopHostPickerMenu(
        SaveSlotSummary save,
        SaveSwapService saveSwapService,
        Action<HostCandidate> onConfirm,
        Action onCancel)
        : base(
            (Game1.viewport.Width - MenuWidth) / 2,
            (Game1.viewport.Height - MenuHeight) / 2,
            MenuWidth,
            MenuHeight,
            showUpperRightCloseButton: false)
    {
        this.save = save;
        this.saveSwapService = saveSwapService;
        this.onConfirm = onConfirm;
        this.onCancel = onCancel;
        this.selectedCandidate = save.Candidates.FirstOrDefault(candidate => candidate.IsCurrentHost) ?? save.Candidates[0];
        this.statusMessage = $"Pick the farmer who should host {save.FarmName}.";

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

        if (this.upButton.containsPoint(x, y))
        {
            this.ScrollCandidates(-1);
            return;
        }

        if (this.downButton.containsPoint(x, y))
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
            this.statusMessage = this.selectedCandidate.IsCurrentHost
                ? $"{this.selectedCandidate.Name} is already the host. START will load the save as-is."
                : $"{this.selectedCandidate.Name} will become host, then the save will start.";
            this.statusColor = Game1.textColor;
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

        SpriteText.drawString(b, "CHOOSE HOST", this.xPositionOnScreen + 32, this.yPositionOnScreen + 24, color: Game1.textColor);
        this.DrawValueLine(b, "SAVE", this.save.FarmName, this.xPositionOnScreen + 34, this.yPositionOnScreen + 76, this.width - 140);
        this.DrawValueLine(b, "CURRENT", this.save.CurrentHostName, this.xPositionOnScreen + 34, this.yPositionOnScreen + 106, this.width - 140);
        this.DrawWrappedPixelText(
            b,
            "Choose a farmer below. Clicking START will load the game immediately.",
            new Rectangle(this.xPositionOnScreen + 34, this.yPositionOnScreen + 136, this.width - 68, 34),
            new Color(80, 50, 20));

        this.DrawPanel(b, this.candidatePanelBounds, "FARMERS");
        this.DrawPanel(b, this.footerBounds, "STATUS");
        this.DrawCandidates(b);
        this.DrawFooter(b);
        this.DrawButton(b, this.confirmButton, this.GetConfirmLabel(), enabled: !this.isWorking);
        this.DrawButton(b, this.cancelButton, "BACK", enabled: !this.isWorking);
        this.DrawArrowButton(b, this.upButton, !this.isWorking && this.candidateScrollIndex > 0, pointsUp: true);
        this.DrawArrowButton(b, this.downButton, !this.isWorking && this.candidateScrollIndex + MaxVisibleCandidates < this.save.Candidates.Count, pointsUp: false);

        this.drawMouse(b);
    }

    private void RefreshLayout()
    {
        this.xPositionOnScreen = (Game1.viewport.Width - MenuWidth) / 2;
        this.yPositionOnScreen = (Game1.viewport.Height - MenuHeight) / 2;
        this.width = MenuWidth;
        this.height = MenuHeight;

        this.upperRightCloseButton.bounds = new Rectangle(this.xPositionOnScreen + this.width - 96, this.yPositionOnScreen + 16, 64, 64);

        this.candidatePanelBounds = new Rectangle(this.xPositionOnScreen + 32, this.yPositionOnScreen + 184, this.width - 64, 412);
        this.footerBounds = new Rectangle(this.xPositionOnScreen + 32, this.yPositionOnScreen + 614, this.width - 64, 114);

        this.confirmButton = new ClickableComponent(new Rectangle(this.footerBounds.Right - 258, this.footerBounds.Y + 30, 214, 52), "Confirm");
        this.cancelButton = new ClickableComponent(new Rectangle(this.footerBounds.X + 18, this.footerBounds.Y + 30, 126, 52), "Cancel");
        this.upButton = new ClickableComponent(new Rectangle(this.candidatePanelBounds.Right - 154, this.candidatePanelBounds.Y + 12, 62, 40), "Up");
        this.downButton = new ClickableComponent(new Rectangle(this.candidatePanelBounds.Right - 82, this.candidatePanelBounds.Y + 12, 62, 40), "Down");

        this.RefreshClickableComponents();
    }

    private void RefreshClickableComponents()
    {
        this.candidateComponents.Clear();

        int candidateListTop = this.candidatePanelBounds.Y + 72;
        int candidateWidth = this.candidatePanelBounds.Width - 48;
        int visibleCount = Math.Min(MaxVisibleCandidates, this.save.Candidates.Count - this.candidateScrollIndex);
        for (int index = 0; index < visibleCount; index++)
        {
            Rectangle bounds = new(
                this.candidatePanelBounds.X + 24,
                candidateListTop + index * (RowHeight + RowGap),
                candidateWidth,
                RowHeight);
            this.candidateComponents.Add(new ClickableComponent(bounds, $"Candidate_{index}"));
        }
    }

    private void DrawPanel(SpriteBatch b, Rectangle bounds, string title)
    {
        IClickableMenu.drawTextureBox(b, bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White);
        SpriteText.drawString(b, title, bounds.X + 18, bounds.Y + 16, color: Game1.textColor);
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
            string roleLabel = candidate.IsCurrentHost ? "CURRENT HOST" : "FARMHAND";
            string homeLabel = string.IsNullOrWhiteSpace(candidate.HomeLocation)
                ? "HOME UNKNOWN"
                : $"HOME {candidate.HomeLocation}";

            SpriteText.drawString(b, TrimPixelText(nameLabel, component.bounds.Width - 28), component.bounds.X + 14, component.bounds.Y + 12, color: Game1.textColor);
            SpriteText.drawString(b, roleLabel, component.bounds.X + 14, component.bounds.Y + 44, color: new Color(20, 92, 52));
            SpriteText.drawString(b, TrimPixelText(homeLabel, component.bounds.Width - 28), component.bounds.X + 14, component.bounds.Y + 74, color: new Color(80, 50, 20));
        }
    }

    private void DrawFooter(SpriteBatch b)
    {
        this.DrawWrappedPixelText(
            b,
            this.statusMessage,
            new Rectangle(this.footerBounds.X + 164, this.footerBounds.Y + 18, this.footerBounds.Width - 440, 76),
            this.statusColor);
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

        int textY = component.bounds.Center.Y - (SpriteText.getHeightOfString(label) / 2);
        SpriteText.drawStringHorizontallyCenteredAt(
            b,
            label,
            component.bounds.Center.X,
            textY,
            color: textColor,
            maxWidth: component.bounds.Width - 20);
    }

    private void DrawArrowButton(SpriteBatch b, ClickableComponent component, bool enabled, bool pointsUp)
    {
        Color tint = enabled ? Color.White : Color.Gray;
        Color color = enabled ? Game1.textColor : new Color(96, 96, 96);
        IClickableMenu.drawTextureBox(b, component.bounds.X, component.bounds.Y, component.bounds.Width, component.bounds.Height, tint);
        this.DrawArrowGlyph(b, component.bounds, color, pointsUp);
    }

    private void DrawWrappedPixelText(SpriteBatch b, string text, Rectangle bounds, Color color)
    {
        SpriteText.drawString(
            b,
            text,
            bounds.X,
            bounds.Y,
            width: bounds.Width,
            height: bounds.Height,
            color: color);
    }

    private void ConfirmSelection()
    {
        try
        {
            this.isWorking = true;
            this.statusMessage = this.selectedCandidate.IsCurrentHost
                ? "Starting the current host..."
                : $"Switching to {this.selectedCandidate.Name}, then starting...";
            this.statusColor = new Color(24, 96, 48);
            Game1.playSound("newArtifact");
            this.onConfirm(this.selectedCandidate);
        }
        catch (Exception ex)
        {
            this.isWorking = false;
            this.statusMessage = $"Could not start the save: {ex.Message}";
            this.statusColor = new Color(128, 32, 32);
            Game1.playSound("cancel");
            this.saveSwapService.LoadSaveSlot(this.save.SaveDirectoryPath);
        }
    }

    private void ScrollCandidates(int delta)
    {
        int maxScroll = Math.Max(0, this.save.Candidates.Count - MaxVisibleCandidates);
        int newValue = Math.Clamp(this.candidateScrollIndex + delta, 0, maxScroll);
        if (newValue == this.candidateScrollIndex)
            return;

        this.candidateScrollIndex = newValue;
        Game1.playSound("shiny4");
        this.RefreshClickableComponents();
    }

    private string GetConfirmLabel()
    {
        return this.selectedCandidate.IsCurrentHost ? "START" : "SWITCH + START";
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

    private static string TrimPixelText(string text, int maxWidth)
    {
        if (SpriteText.getWidthOfString(text) <= maxWidth)
            return text;

        const string ellipsis = "...";
        int length = text.Length;
        while (length > 1)
        {
            string candidate = text[..length] + ellipsis;
            if (SpriteText.getWidthOfString(candidate) <= maxWidth)
                return candidate;

            length--;
        }

        return ellipsis;
    }

    private void DrawValueLine(SpriteBatch b, string label, string value, int x, int y, int maxWidth)
    {
        string text = $"{label}: {value}";
        SpriteText.drawString(b, TrimPixelText(text, maxWidth), x, y, color: Game1.textColor);
    }
}
