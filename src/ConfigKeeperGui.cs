using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ConfigKeeper
{
    /// <summary>
    /// The main GUI dialog for ConfigKeeper — lets the player select categories,
    /// export settings to a backup zip, pick from existing backups, and import.
    /// </summary>
    public class ConfigKeeperGui : GuiDialog
    {
        private readonly ConfigKeeperModSystem mod;

        // Category toggle states
        private Dictionary<string, bool> categoryStates = new Dictionary<string, bool>
        {
            { "clientsettings", true },
            { "serversettings", true },
            { "modconfig", true },
            { "macros", true },
            { "serverfavorites", true },
            { "installedmods", true }
        };

        private List<BackupInfo> availableBackups = new List<BackupInfo>();
        private int selectedBackupIndex = 0;
        private string statusText = "";
        private bool showingConfirmDialog = false;

        public override string ToggleKeyCombinationCode => "configkeeper";

        public ConfigKeeperGui(ICoreClientAPI capi, ConfigKeeperModSystem mod) : base(capi)
        {
            this.mod = mod;
        }

        private void ComposeDialog()
        {
            // Refresh backup list
            availableBackups = mod.FindBackups();

            // Layout dimensions
            double width = 520;
            double headerHeight = 40;
            double categoryBlockHeight = 180;
            double backupSelectorHeight = availableBackups.Count > 0 ? 70 : 0;
            double buttonRowHeight = 50;
            double statusHeight = 40;
            double totalHeight = headerHeight + categoryBlockHeight + backupSelectorHeight + buttonRowHeight + statusHeight + 40;

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            double yPos = 0;

            var composer = capi.Gui.CreateCompo("configkeeper-gui", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("configkeeper:gui-title"), OnTitleBarClose)
                .BeginChildElements(bgBounds);

            yPos += 10;

            // Section label
            composer.AddStaticText(
                Lang.Get("configkeeper:lbl-categories"),
                CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold),
                ElementBounds.Fixed(0, yPos, width, 25)
            );
            yPos += 30;

            // Category toggles
            string[] categoryKeys = { "clientsettings", "serversettings", "modconfig", "macros", "serverfavorites", "installedmods" };
            string[] categoryLangKeys = {
                "configkeeper:cat-clientsettings",
                "configkeeper:cat-serversettings",
                "configkeeper:cat-modconfig",
                "configkeeper:cat-macros",
                "configkeeper:cat-serverfavorites",
                "configkeeper:cat-installedmods"
            };

            for (int i = 0; i < categoryKeys.Length; i++)
            {
                string key = categoryKeys[i];
                composer.AddSwitch(
                    (val) => OnCategoryToggle(key, val),
                    ElementBounds.Fixed(0, yPos, 30, 25),
                    key + "-switch",
                    30
                );
                composer.AddStaticText(
                    Lang.Get(categoryLangKeys[i]),
                    CairoFont.WhiteSmallText(),
                    ElementBounds.Fixed(45, yPos + 2, width - 50, 25)
                );
                yPos += 30;
            }

            yPos += 10;

            // Backup selector (only show if backups exist)
            if (availableBackups.Count > 0)
            {
                composer.AddStaticText(
                    "Available Backups:",
                    CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold),
                    ElementBounds.Fixed(0, yPos, width, 25)
                );
                yPos += 28;

                string[] backupNames = availableBackups.Select(b => b.DisplayName).ToArray();
                string[] backupValues = availableBackups.Select((b, idx) => idx.ToString()).ToArray();

                composer.AddDropDown(
                    backupValues,
                    backupNames,
                    selectedBackupIndex,
                    OnBackupSelected,
                    ElementBounds.Fixed(0, yPos, width, 30),
                    "backup-dropdown"
                );
                yPos += 40;
            }

            // Buttons row
            double btnWidth = availableBackups.Count > 0 ? (width - 20) / 3.0 : (width - 10) / 2.0;

            composer.AddSmallButton(
                Lang.Get("configkeeper:btn-export"),
                OnExportPressed,
                ElementBounds.Fixed(0, yPos, btnWidth, 40),
                EnumButtonStyle.Normal
            );

            if (availableBackups.Count > 0)
            {
                composer.AddSmallButton(
                    Lang.Get("configkeeper:btn-import"),
                    OnImportPressed,
                    ElementBounds.Fixed(btnWidth + 10, yPos, btnWidth, 40),
                    EnumButtonStyle.Normal
                );

                composer.AddSmallButton(
                    Lang.Get("configkeeper:btn-close"),
                    OnClosePressed,
                    ElementBounds.Fixed((btnWidth + 10) * 2, yPos, btnWidth, 40),
                    EnumButtonStyle.Normal
                );
            }
            else
            {
                composer.AddSmallButton(
                    Lang.Get("configkeeper:btn-close"),
                    OnClosePressed,
                    ElementBounds.Fixed(btnWidth + 10, yPos, btnWidth, 40),
                    EnumButtonStyle.Normal
                );
            }

            yPos += 50;

            // Status text — size the box to the actual (word-wrapped) text so the
            // auto-sizing dialog reserves enough room and nothing is clipped.
            var statusFont = CairoFont.WhiteSmallText();
            double statusTextHeight = 50;
            if (!string.IsNullOrEmpty(statusText))
            {
                double scaledHeight = capi.Gui.Text.GetMultilineTextHeight(statusFont, statusText, width * RuntimeEnv.GUIScale);
                statusTextHeight = Math.Max(50, scaledHeight / RuntimeEnv.GUIScale + 10);
            }

            composer.AddDynamicText(
                statusText,
                statusFont,
                ElementBounds.Fixed(0, yPos, width, statusTextHeight),
                "status-text"
            );

            composer.EndChildElements();
            SingleComposer = composer.Compose();

            // Set initial switch states
            for (int i = 0; i < categoryKeys.Length; i++)
            {
                var sw = SingleComposer.GetSwitch(categoryKeys[i] + "-switch");
                if (sw != null)
                {
                    sw.SetValue(categoryStates[categoryKeys[i]]);
                }
            }
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            statusText = "";
            ComposeDialog();
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }

        private void OnCategoryToggle(string key, bool value)
        {
            categoryStates[key] = value;
        }

        private void OnBackupSelected(string val, bool selected)
        {
            if (int.TryParse(val, out int idx))
            {
                selectedBackupIndex = idx;
            }
        }

        private bool OnExportPressed()
        {
            var result = mod.ExportSettings(categoryStates);

            // Recompose to update status and refresh backup list
            SetStatus(result.Success
                ? $"Exported successfully!\n{string.Join(", ", result.ItemsExported)}\nSaved to: {result.FilePath}"
                : $"Export failed: {result.Message}");

            return true;
        }

        private bool OnImportPressed()
        {
            if (availableBackups.Count == 0 || selectedBackupIndex >= availableBackups.Count)
            {
                SetStatus("No backup selected.");
                return true;
            }

            if (!showingConfirmDialog)
            {
                showingConfirmDialog = true;
                SetStatus(Lang.Get("configkeeper:import-confirm") + "\nPress Import again to confirm, or Close to cancel.");
                return true;
            }

            // Second press = confirmed
            showingConfirmDialog = false;
            var backup = availableBackups[selectedBackupIndex];
            var result = mod.ImportSettings(backup.FilePath, categoryStates);

            SetStatus(result.Success
                ? $"Imported {result.ItemsImported.Count} files successfully!\nClient settings are applied now; keybinds and server/mod configs take effect after restarting the game."
                : $"Import failed: {result.Message}");

            return true;
        }

        private bool OnClosePressed()
        {
            showingConfirmDialog = false;
            TryClose();
            return true;
        }

        private void SetStatus(string text)
        {
            // Recompose so the status box is resized to fit the new text.
            statusText = text;
            ComposeDialog();
        }
    }
}
