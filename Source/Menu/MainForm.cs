﻿// COPYRIGHT 2009, 2010, 2011, 2012, 2013, 2014, 2015 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using GNU.Gettext;
using GNU.Gettext.WinForms;

using Orts.Common;
using Orts.Common.Native;
using Orts.Formats.Msts;
using Orts.Formats.OR.Files;
using Orts.Formats.OR.Models;
using Orts.Menu.Entities;
using Orts.Settings;
using Orts.Updater;

using Path = Orts.Menu.Entities.Path;

namespace Orts.Menu
{
    public partial class MainForm : Form
    {
        public enum UserAction
        {
            SingleplayerNewGame,
            SingleplayerResumeSave,
            SingleplayerReplaySave,
            SingleplayerReplaySaveFromSave,
            MultiplayerServer,
            MultiplayerClient,
            SinglePlayerTimetableGame,
            SinglePlayerResumeTimetableGame,
            MultiplayerServerResumeSave,
            MultiplayerClientResumeSave
        }

        private bool initialized;
        private UserSettings settings;
        private IEnumerable<Folder> folders = new Folder[0];
        private IEnumerable<Route> routes = new Route[0];
        private IEnumerable<Activity> activities = new Activity[0];
        private IEnumerable<Consist> consists = new Consist[0];
        private IEnumerable<Path> paths = new Path[0];
        private IEnumerable<TimetableInfo> timetableSets = new TimetableInfo[0];
        private IEnumerable<WeatherFileInfo> timetableWeatherFileSet = new WeatherFileInfo[0];
        private CancellationTokenSource ctsRouteLoading;
        private CancellationTokenSource ctsActivityLoading;
        private CancellationTokenSource ctsConsistLoading;
        private CancellationTokenSource ctsPathLoading;
        private CancellationTokenSource ctsTimeTableLoading;

        private readonly ResourceManager resources = new ResourceManager("Orts.Menu.Properties.Resources", typeof(MainForm).Assembly);
        private UpdateManager updateManager;
        private readonly Image elevationIcon;
        private int detailUpdater;

        internal string RunActivityProgram
        {
            get
            {
                return System.IO.Path.Combine(Application.StartupPath, "ActivityRunner.exe"); ;
            }
        }

        // Base items
        public Folder SelectedFolder { get { return (Folder)comboBoxFolder.SelectedItem; } }
        public Route SelectedRoute { get { return (Route)comboBoxRoute.SelectedItem; } }

        // Activity mode items
        public Activity SelectedActivity { get { return (Activity)comboBoxActivity.SelectedItem; } }
        public Consist SelectedConsist { get { return (Consist)comboBoxConsist.SelectedItem; } }
        public Path SelectedPath { get { return (Path)comboBoxHeadTo.SelectedItem; } }
        public string SelectedStartTime { get { return comboBoxStartTime.Text; } }

        // Timetable mode items
        public TimetableInfo SelectedTimetableSet { get { return (TimetableInfo)comboBoxTimetableSet.SelectedItem; } }
        public TimetableFile SelectedTimetable { get { return (TimetableFile)comboBoxTimetable.SelectedItem; } }
        public TrainInformation SelectedTimetableTrain { get { return (TrainInformation)comboBoxTimetableTrain.SelectedItem; } }
        public int SelectedTimetableDay { get { return initialized ? (comboBoxTimetableDay.SelectedItem as KeyedComboBoxItem).Key : 0; } }
        public WeatherFileInfo SelectedWeatherFile { get { return (WeatherFileInfo)comboBoxTimetableWeatherFile.SelectedItem; } }
        public Consist SelectedTimetableConsist;
        public Path SelectedTimetablePath;

        // Shared items
        public int SelectedStartSeason { get { return initialized ? (radioButtonModeActivity.Checked ? (comboBoxStartSeason.SelectedItem as KeyedComboBoxItem).Key : (comboBoxTimetableSeason.SelectedItem as KeyedComboBoxItem).Key) : 0; } }
        public int SelectedStartWeather { get { return initialized ? (radioButtonModeActivity.Checked ? (comboBoxStartWeather.SelectedItem as KeyedComboBoxItem).Key : (comboBoxTimetableWeather.SelectedItem as KeyedComboBoxItem).Key) : 0; } }

        public string SelectedSaveFile { get; set; }
        public UserAction SelectedAction { get; set; }

        private GettextResourceManager catalog = new GettextResourceManager("Menu");

        #region Main Form
        public MainForm()
        {
            InitializeComponent();

            // Windows 2000 and XP should use 8.25pt Tahoma, while Windows
            // Vista and later should use 9pt "Segoe UI". We'll use the
            // Message Box font to allow for user-customizations, though.
            Font = SystemFonts.MessageBoxFont;

            // Set title to show revision or build info.
            Text = VersionInfo.Version.Length > 0 ? $"{Application.ProductName} {VersionInfo.Version}" : $"{Application.ProductName} build {VersionInfo.Build}";
#if DEBUG
            Text += " (debug)";
#endif
            panelModeTimetable.Location = panelModeActivity.Location;
            UpdateEnabled();
            elevationIcon = new Icon(SystemIcons.Shield, SystemInformation.SmallIconSize).ToBitmap();
        }

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            var options = Environment.GetCommandLineArgs().Where(a => (a.StartsWith("-") || a.StartsWith("/"))).Select(a => a.Substring(1));
            settings = new UserSettings(options);

            List<Task> initTasks = new List<Task>
            {
                LoadFolderListAsync()
            };

            LoadOptions();
            LoadLanguage();

            if (!initialized)
            {
                initTasks.Add(InitializeUpdateManager());
                initTasks.Add(LoadToolsAndDocuments());

                var seasons = new[] {
                    new KeyedComboBoxItem(0, catalog.GetString("Spring")),
                    new KeyedComboBoxItem(1, catalog.GetString("Summer")),
                    new KeyedComboBoxItem(2, catalog.GetString("Autumn")),
                    new KeyedComboBoxItem(3, catalog.GetString("Winter")),
                };
                var weathers = new[] {
                    new KeyedComboBoxItem(0, catalog.GetString("Clear")),
                    new KeyedComboBoxItem(1, catalog.GetString("Snow")),
                    new KeyedComboBoxItem(2, catalog.GetString("Rain")),
                };
                var difficulties = new[] {
                    catalog.GetString("Easy"),
                    catalog.GetString("Medium"),
                    catalog.GetString("Hard"),
                    "",
                };
                var days = new[] {
                    new KeyedComboBoxItem(0, catalog.GetString("Monday")),
                    new KeyedComboBoxItem(1, catalog.GetString("Tuesday")),
                    new KeyedComboBoxItem(2, catalog.GetString("Wednesday")),
                    new KeyedComboBoxItem(3, catalog.GetString("Thursday")),
                    new KeyedComboBoxItem(4, catalog.GetString("Friday")),
                    new KeyedComboBoxItem(5, catalog.GetString("Saturday")),
                    new KeyedComboBoxItem(6, catalog.GetString("Sunday")),
                };

                comboBoxStartSeason.Items.AddRange(seasons);
                comboBoxStartWeather.Items.AddRange(weathers);
                comboBoxDifficulty.Items.AddRange(difficulties);

                comboBoxTimetableSeason.Items.AddRange(seasons);
                comboBoxTimetableWeather.Items.AddRange(weathers);
                comboBoxTimetableDay.Items.AddRange(days);

                initialized = true;
            }

            ShowEnvironment();
            ShowTimetableEnvironment();

            await Task.WhenAll(initTasks);
        }

        private async Task InitializeUpdateManager()
        {
            updateManager = await UpdateManager.Initialize(System.IO.Path.GetDirectoryName(Application.ExecutablePath), Application.ProductName, VersionInfo.VersionOrBuild);
            await CheckForUpdateAsync();
        }

        private async Task<IEnumerable<ToolStripItem>> LoadTools()
        {
            var coreExecutables = new[] {
                    "OpenRails.exe",
                    "Menu.exe",
                    "ActivityRunner.exe",
                    "Updater.exe",
                };
            return await Task.Run(() =>
            {
                return Directory.GetFiles(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "*.exe").
                Where(fileName => (!coreExecutables.Contains(System.IO.Path.GetFileName(fileName), StringComparer.OrdinalIgnoreCase))).
                Select(fileName =>
                {
                    FileVersionInfo toolInfo = FileVersionInfo.GetVersionInfo(fileName);
                    // Skip any executable that isn't part of this product (e.g. Visual Studio hosting files).
                    if (toolInfo.ProductName != Application.ProductName)
                        return null;
                    string toolName = catalog.GetString(toolInfo.FileDescription.Replace(Application.ProductName, "").Trim());
                    return new ToolStripMenuItem(toolName, null, (object sender2, EventArgs e2) =>
                    {
                        string toolPath = (sender2 as ToolStripItem).Tag as string;
                        bool toolIsConsole = false;
                        using (var reader = new BinaryReader(File.OpenRead(toolPath)))
                        {
                            toolIsConsole = GetImageSubsystem(reader) == ImageSubsystem.WindowsConsole;
                        }
                        if (toolIsConsole)
                            Process.Start("cmd", $"/k \"{toolPath}\"");
                        else
                            Process.Start(toolPath);
                    }
                    )
                    { Tag = fileName };
                }).
                    Where(t => t != null);
            }).ConfigureAwait(false);
        }

        private async Task<IEnumerable<ToolStripItem>> LoadDocuments()
        {
            string path = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Documentation");
            if (Directory.Exists(path))
            {
                return await Task.Run(() =>
                {
                    return Directory.GetFiles(path).Select(fileName =>
                    {
                        // These are the following formats that can be selected.
                        if (fileName.EndsWith(".pdf") || fileName.EndsWith(".doc") || fileName.EndsWith(".docx") || fileName.EndsWith(".pptx") || fileName.EndsWith(".txt"))
                        {
                            return new ToolStripMenuItem(System.IO.Path.GetFileName(fileName), null, (Object sender2, EventArgs e2) =>
                            {
                                var docPath = (sender2 as ToolStripItem).Tag as string;
                                Process.Start(docPath);
                            })
                            { Tag = fileName };
                        }
                        return null;
                    }).Where(d => d != null);
                }).ConfigureAwait(false);
            }
            return new ToolStripItem[0];
        }

        private async Task LoadToolsAndDocuments()
        {
            await Task.WhenAll(
                LoadTools().ContinueWith((tools) =>
                {
                    // Add all the tools in alphabetical order.
                    contextMenuStripTools.Items.AddRange((from tool in tools.Result
                                                          orderby tool.Text
                                                          select tool).ToArray());

                }),
                // Just like above, buttonDocuments is a button that is treated like a menu.  The result is a button that acts like a combobox.
                // Populate buttonDocuments.
                LoadDocuments().ContinueWith((documents) =>
                {
                    // Add all the tools in alphabetical order.
                    contextMenuStripDocuments.Items.AddRange((from doc in documents.Result
                                                              orderby doc.Text
                                                              select doc).ToArray());

                }));
            // Documents button will be disabled if Documentation folder is not present.
            buttonDocuments.Enabled = contextMenuStripDocuments.Items.Count > 0;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveOptions();
            if (null != ctsRouteLoading && !ctsRouteLoading.IsCancellationRequested)
                ctsRouteLoading.Cancel();
            if (null != ctsActivityLoading && !ctsActivityLoading.IsCancellationRequested)
                ctsActivityLoading.Cancel();
            if (null != ctsConsistLoading && !ctsConsistLoading.IsCancellationRequested)
                ctsConsistLoading.Cancel();
            if (null != ctsPathLoading && !ctsPathLoading.IsCancellationRequested)
                ctsPathLoading.Cancel();
            if (null != ctsTimeTableLoading && !ctsPathLoading.IsCancellationRequested)
                ctsTimeTableLoading.Cancel();

            // Remove any deleted saves
            if (Directory.Exists(UserSettings.DeletedSaveFolder))
                Directory.Delete(UserSettings.DeletedSaveFolder, true);   // true removes all contents as well as folder
        }

        private async Task CheckForUpdateAsync()
        {
            if (string.IsNullOrEmpty(updateManager.ChannelName))
            {
                linkLabelChangeLog.Visible = false;
                linkLabelUpdate.Visible = false;
                return;
            }
            // This is known directly from the chosen channel so doesn't need to wait for the update check itself.
            linkLabelChangeLog.Visible = !string.IsNullOrEmpty(updateManager.ChangeLogLink);

            //            await Task.Run(() => UpdateManager.CheckForUpdateAsync());
            await updateManager.CheckForUpdateAsync();

            if (updateManager.LastCheckError != null)
                linkLabelUpdate.Text = catalog.GetString("Update check failed");
            else if (updateManager.LastUpdate != null && updateManager.LastUpdate.Version != VersionInfo.Version)
                linkLabelUpdate.Text = catalog.GetStringFmt("Update to {0}", updateManager.LastUpdate.Version);
            else
                linkLabelUpdate.Text = "";
            linkLabelUpdate.Enabled = true;
            linkLabelUpdate.Visible = linkLabelUpdate.Text.Length > 0;
            // Update link's elevation icon and size/position.
            if (updateManager.LastCheckError == null && updateManager.LastUpdate?.Version != VersionInfo.Version && updateManager.UpdaterNeedsElevation)
                linkLabelUpdate.Image = elevationIcon;
            else
                linkLabelUpdate.Image = null;
            linkLabelUpdate.AutoSize = true;
            linkLabelUpdate.Left = panelDetails.Right - linkLabelUpdate.Width - elevationIcon.Width;
            linkLabelUpdate.AutoSize = false;
            linkLabelUpdate.Width = panelDetails.Right - linkLabelUpdate.Left;
        }

        private void LoadLanguage()
        {
            if (!string.IsNullOrEmpty(settings.Language))
            {
                try
                {
                    CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(settings.Language);
                }
                catch
                {
                }
            }

            Localizer.Localize(this, catalog);
        }

        private void RestartMenu()
        {
            Process.Start(Application.ExecutablePath);
            Close();
        }
        #endregion

        #region Folders
        private async void ComboBoxFolder_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                await Task.WhenAll(LoadRouteListAsync(), LoadLocomotiveListAsync());
            }
            catch (TaskCanceledException) { }
        }
        #endregion

        #region Routes
        private async void ComboBoxRoute_SelectedIndexChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            try
            {
                await Task.WhenAll(
                    LoadActivityListAsync(),
                    LoadStartAtListAsync(),
                    LoadTimetableSetListAsync());
            }
            catch (TaskCanceledException) { }
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Mode
        private void RadioButtonMode_CheckedChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            panelModeActivity.Visible = radioButtonModeActivity.Checked;
            panelModeTimetable.Visible = radioButtonModeTimetable.Checked;
            UpdateEnabled();
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Activities
        private void ComboBoxActivity_SelectedIndexChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            ShowLocomotiveList();
            ShowConsistList();
            ShowStartAtList();
            ShowEnvironment();
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
            //Debrief Activity Eval
            //0 = "- Explore route -"
            //1 = "+ Explore in Activity mode +"
            if (comboBoxActivity.SelectedIndex < 2)
            { checkDebriefActivityEval.Checked = false; checkDebriefActivityEval.Enabled = false; }
            else
            { checkDebriefActivityEval.Enabled = true; }
        }
        #endregion

        #region Locomotives
        private void ComboBoxLocomotive_SelectedIndexChanged(object sender, EventArgs e)
        {
            ShowConsistList();
        }
        #endregion

        #region Consists
        private void ComboBoxConsist_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(true);
        }
        #endregion

        #region Starting from
        private void ComboBoxStartAt_SelectedIndexChanged(object sender, EventArgs e)
        {
            ShowHeadToList();
        }
        #endregion

        #region Heading to
        private void ComboBoxHeadTo_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(true);
        }
        #endregion

        #region Environment
        private void ComboBoxStartTime_TextChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(false);
        }

        private void ComboBoxStartSeason_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(false);
        }

        private void ComboBoxStartWeather_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateExploreActivity(false);
        }
        #endregion

        #region Timetable Sets
        private void ComboBoxTimetableSet_SelectedIndexChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            UpdateTimetableSet();
            ShowTimetableList();
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Timetables
        private void ComboBoxTimetable_selectedIndexChanged(object sender, EventArgs e)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            ShowTimetableTrainList();
            if (updater == 0)
            {
                ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Timetable Trains
        private async void ComboBoxTimetableTrain_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxTimetableTrain.SelectedItem is TrainInformation selectedTrain)
            {
                int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
                SelectedTimetableConsist = await Consist.GetConsist(SelectedFolder, selectedTrain.LeadingConsist, selectedTrain.ReverseConsist, CancellationToken.None);
                SelectedTimetablePath = Path.GetPath(SelectedRoute, selectedTrain.Path, false);
                if (updater == 0)
                {
                    ShowDetails();
                    detailUpdater = 0;
                }
            }
        }
        #endregion

        #region Timetable environment
        private void ComboBoxTimetableDay_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateTimetableSet();
        }

        private void ComboBoxTimetableSeason_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateTimetableSet();
        }

        private void ComboBoxTimetableWeather_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateTimetableSet();
        }

        void ComboBoxTimetableWeatherFile_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateTimetableWeatherSet();
        }
        #endregion

        #region Multiplayer
        private void TextBoxMPUser_TextChanged(object sender, EventArgs e)
        {
            UpdateEnabled();
        }

        private bool CheckUserName(string text)
        {
            string tmp = text;
            if (tmp.Length < 4 || tmp.Length > 10 || tmp.Contains("\"") || tmp.Contains("\'") || tmp.Contains(" ") || tmp.Contains("-") || Char.IsDigit(tmp, 0))
            {
                MessageBox.Show(catalog.GetString("User name must be 4-10 characters long, cannot contain space, ', \" or - and must not start with a digit."), Application.ProductName);
                return false;
            }
            return true;
        }

        #endregion

        #region Misc. buttons and options
        private async void LinkLabelUpdate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (updateManager.LastCheckError != null)
            {
                MessageBox.Show(catalog.GetStringFmt("The update check failed due to an error:\n\n{0}", updateManager.LastCheckError), Application.ProductName);
                return;
            }

            await updateManager.RunUpdateProcess();

            if (updateManager.LastUpdateError != null)
            {
                MessageBox.Show(catalog.GetStringFmt("The update failed due to an error:\n\n{0}", updateManager.LastUpdateError), Application.ProductName);
            }
        }

        private void LinkLabelChangeLog_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(updateManager.ChangeLogLink);
        }

        private void ButtonTools_Click(object sender, EventArgs e)
        {
            contextMenuStripTools.Show(buttonTools, new Point(0, buttonTools.ClientSize.Height), ToolStripDropDownDirection.Default);
        }

        private void ButtonDocuments_Click(object sender, EventArgs e)
        {
            contextMenuStripDocuments.Show(buttonDocuments, new Point(0, buttonDocuments.ClientSize.Height), ToolStripDropDownDirection.Default);
        }

        private void TestingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new TestingForm(settings, this.RunActivityProgram))
            {
                form.ShowDialog(this);
            }
        }

        private async void ButtonOptions_Click(object sender, EventArgs e)
        {
            SaveOptions();

            using (var form = new OptionsForm(settings, updateManager, false))
            {
                switch (form.ShowDialog(this))
                {
                    case DialogResult.OK:
                        await Task.WhenAll(LoadFolderListAsync(), CheckForUpdateAsync());
                        break;
                    case DialogResult.Retry:
                        RestartMenu();
                        break;
                }
            }
        }

        private void ButtonStart_Click(object sender, EventArgs e)
        {
            SaveOptions();

            if (radioButtonModeActivity.Checked)
            {
                SelectedAction = UserAction.SingleplayerNewGame;
                if (SelectedActivity != null)
                    DialogResult = DialogResult.OK;
            }
            else
            {
                SelectedAction = UserAction.SinglePlayerTimetableGame;
                if (SelectedTimetableTrain != null)
                    DialogResult = DialogResult.OK;
            }
        }

        private void ButtonResume_Click(object sender, EventArgs e)
        {
            OpenResumeForm(false);
        }

        void ButtonResumeMP_Click(object sender, EventArgs e)
        {
            OpenResumeForm(true);
        }

        void OpenResumeForm(bool multiplayer)
        {
            if (radioButtonModeTimetable.Checked)
            {
                SelectedAction = UserAction.SinglePlayerTimetableGame;
            }
            else if (!multiplayer)
            {
                SelectedAction = UserAction.SingleplayerNewGame;
            }
            else if (radioButtonMPClient.Checked)
            {
                SelectedAction = UserAction.MultiplayerClient;
            }
            else
                SelectedAction = UserAction.MultiplayerServer;

            // if timetable mode but no timetable selected - no action
            if (SelectedAction == UserAction.SinglePlayerTimetableGame && (SelectedTimetableSet == null || multiplayer))
            {
                return;
            }

            using (var form = new ResumeForm(settings, SelectedRoute, SelectedAction, SelectedActivity, SelectedTimetableSet, routes))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    SaveOptions();
                    SelectedSaveFile = form.SelectedSaveFile;
                    SelectedAction = form.SelectedAction;
                    DialogResult = DialogResult.OK;
                }
            }
        }

        void ButtonStartMP_Click(object sender, EventArgs e)
        {
            if (CheckUserName(textBoxMPUser.Text) == false) return;
            SaveOptions();
            SelectedAction = radioButtonMPClient.Checked ? UserAction.MultiplayerClient : UserAction.MultiplayerServer;
            DialogResult = DialogResult.OK;
        }

        #endregion

        #region Options
        private void LoadOptions()
        {
            checkBoxWarnings.Checked = settings.Logging;
            //Debrief activity evaluation
            checkDebriefActivityEval.Checked = settings.DebriefActivityEval;
            //TO DO: Debrief TTactivity evaluation
            //checkDebriefTTActivityEval.Checked = Settings.DebriefTTActivityEval;

            textBoxMPUser.Text = settings.Multiplayer_User;
            textBoxMPHost.Text = settings.Multiplayer_Host + ":" + settings.Multiplayer_Port;
        }

        private void SaveOptions()
        {
            settings.Logging = checkBoxWarnings.Checked;
            settings.Multiplayer_User = textBoxMPUser.Text;
            //Debrief activity evaluation
            settings.DebriefActivityEval = checkDebriefActivityEval.Checked;
            //TO DO: Debrief TTactivity evaluation
            //Settings.DebriefTTActivityEval = checkDebriefTTActivityEval.Checked;

            var mpHost = textBoxMPHost.Text.Split(':');
            settings.Multiplayer_Host = mpHost[0];
            if (mpHost.Length > 1)
            {
                var port = settings.Multiplayer_Port;
                if (int.TryParse(mpHost[1], out port))
                    settings.Multiplayer_Port = port;
            }
            else
            {
                settings.Multiplayer_Port = (int)settings.GetDefaultValue("Multiplayer_Port");
            }
            settings.Menu_Selection = new[] {
                // Base items
                SelectedFolder?.Path ?? string.Empty,
                SelectedRoute?.Path ?? string.Empty,
                // Activity mode items / Explore mode items
                radioButtonModeActivity.Checked ? SelectedActivity?.FilePath ?? string.Empty : SelectedTimetableSet?.FileName ?? string.Empty,
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity && (comboBoxLocomotive.SelectedItem as Locomotive)?.FilePath != null ? (comboBoxLocomotive.SelectedItem as Locomotive).FilePath : string.Empty :
                    SelectedTimetable?.Description ?? string.Empty,
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity && SelectedConsist != null ? SelectedConsist.FilePath : string.Empty :
                    SelectedTimetableTrain?.Column.ToString() ?? string.Empty,
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity && SelectedPath != null ? SelectedPath.FilePath : string.Empty : SelectedTimetableDay.ToString(),
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity ? SelectedStartTime : string.Empty : string.Empty,
                // Shared items
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity ? SelectedStartSeason.ToString() : string.Empty : SelectedStartSeason.ToString(),
                radioButtonModeActivity.Checked ?
                    SelectedActivity is ExploreActivity ? SelectedStartWeather.ToString() : string.Empty : SelectedStartWeather.ToString(),
            };
            settings.Save();
        }
        #endregion

        #region Enabled state
        private void UpdateEnabled()
        {
            comboBoxFolder.Enabled = comboBoxFolder.Items.Count > 0;
            comboBoxRoute.Enabled = comboBoxRoute.Items.Count > 0;
            comboBoxActivity.Enabled = comboBoxActivity.Items.Count > 0;
            comboBoxLocomotive.Enabled = comboBoxLocomotive.Items.Count > 0 && SelectedActivity is ExploreActivity;
            comboBoxConsist.Enabled = comboBoxConsist.Items.Count > 0 && SelectedActivity is ExploreActivity;
            comboBoxStartAt.Enabled = comboBoxStartAt.Items.Count > 0 && SelectedActivity is ExploreActivity;
            comboBoxHeadTo.Enabled = comboBoxHeadTo.Items.Count > 0 && SelectedActivity is ExploreActivity;
            comboBoxStartTime.Enabled = comboBoxStartSeason.Enabled = comboBoxStartWeather.Enabled = SelectedActivity is ExploreActivity;
            comboBoxStartTime.DropDownStyle = SelectedActivity is ExploreActivity ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            comboBoxTimetable.Enabled = comboBoxTimetableSet.Items.Count > 0;
            comboBoxTimetableTrain.Enabled = comboBoxTimetable.Items.Count > 0;
            comboBoxTimetableWeatherFile.Enabled = comboBoxTimetableWeatherFile.Items.Count > 0;
            //Avoid to Start with a non valid Activity/Locomotive/Consist.
            buttonResume.Enabled = buttonStart.Enabled = radioButtonModeActivity.Checked && !comboBoxActivity.Text.StartsWith("<") && !comboBoxLocomotive.Text.StartsWith("<") ?
                SelectedActivity != null && (!(SelectedActivity is ExploreActivity) || (comboBoxConsist.Items.Count > 0 && comboBoxHeadTo.Items.Count > 0)) :
                SelectedTimetableTrain != null;
            buttonResumeMP.Enabled = buttonStartMP.Enabled = buttonStart.Enabled && !String.IsNullOrEmpty(textBoxMPUser.Text) && !String.IsNullOrEmpty(textBoxMPHost.Text);
        }
        #endregion

        #region Folder list
        private async Task LoadFolderListAsync()
        {
            try
            {
                folders = (await Folder.GetFolders(settings)).OrderBy(f => f.Name);
            }
            catch (TaskCanceledException)
            {
                folders = new Folder[0];
            }

            ShowFolderList();
            if (folders.Count() > 0)
                comboBoxFolder.Focus();

            if (!initialized && folders.Count() == 0)
            {
                using (var form = new OptionsForm(settings, updateManager, true))
                {
                    switch (form.ShowDialog(this))
                    {
                        case DialogResult.OK:
                            await LoadFolderListAsync();
                            break;
                        case DialogResult.Retry:
                            RestartMenu();
                            break;
                    }
                }
            }
        }

        private void ShowFolderList()
        {
            try
            {
                comboBoxFolder.BeginUpdate();
                comboBoxFolder.Items.Clear();
                comboBoxFolder.Items.AddRange(folders.ToArray());
            }
            finally
            {
                comboBoxFolder.EndUpdate();
            }
            UpdateFromMenuSelection<Folder>(comboBoxFolder, Menu_SelectionIndex.Folder, f => f.Path);
            UpdateEnabled();
        }
        #endregion

        #region Route list
        private async Task LoadRouteListAsync()
        {
            lock (routes)
            {
                if (ctsRouteLoading != null && !ctsRouteLoading.IsCancellationRequested)
                    ctsRouteLoading.Cancel();
                ctsRouteLoading = ResetCancellationTokenSource(ctsRouteLoading);
            }
            paths = new Path[0];
            activities = new Activity[0];

            Folder selectedFolder = SelectedFolder;
            try
            {
                routes = (await Route.GetRoutes(selectedFolder, ctsRouteLoading.Token)).OrderBy(r => r.Name);
            }
            catch (TaskCanceledException)
            {
                routes = new Route[0];
            }
            //cleanout existing data
            ShowRouteList();
            ShowActivityList();
            ShowStartAtList();
            ShowHeadToList();

        }

        private void ShowRouteList()
        {
            try
            {
                comboBoxRoute.BeginUpdate();
                comboBoxRoute.Items.Clear();
                comboBoxRoute.Items.AddRange(routes.ToArray());
            }
            finally
            {
                comboBoxRoute.EndUpdate();
            }
            UpdateFromMenuSelection<Route>(comboBoxRoute, Menu_SelectionIndex.Route, r => r.Path);
            if (settings.Menu_Selection.Length > (int)Menu_SelectionIndex.Activity)
            {
                string path = settings.Menu_Selection[(int)Menu_SelectionIndex.Activity]; // Activity or Timetable
                string extension = System.IO.Path.GetExtension(path).ToLower();
                if (extension == ".act")
                    radioButtonModeActivity.Checked = true;
                else if (extension == ".timetable_or")
                    radioButtonModeTimetable.Checked = true;
            }
            UpdateEnabled();
        }
        #endregion

        #region Activity list
        private async Task LoadActivityListAsync()
        {
            lock (activities)
            {
                if (ctsActivityLoading != null && !ctsActivityLoading.IsCancellationRequested)
                    ctsActivityLoading.Cancel();
                ctsActivityLoading = ResetCancellationTokenSource(ctsActivityLoading);
            }
            //            ShowActivityList();

            Folder selectedFolder = SelectedFolder;
            Route selectedRoute = SelectedRoute;
            try
            {
                activities = (await Activity.GetActivities(selectedFolder, selectedRoute, ctsActivityLoading.Token)).OrderBy(a => a.Name);
            }
            catch (TaskCanceledException)
            {
                activities = new Activity[0];
            }
            ShowActivityList();
        }

        private void ShowActivityList()
        {
            try
            {
                comboBoxActivity.BeginUpdate();
                comboBoxActivity.Items.Clear();
                comboBoxActivity.Items.AddRange(activities.ToArray());
            }
            finally
            {
                comboBoxActivity.EndUpdate();
            }
            UpdateFromMenuSelection<Activity>(comboBoxActivity, Menu_SelectionIndex.Activity, a => a.FilePath);
            UpdateEnabled();
        }

        private void UpdateExploreActivity(bool updateDetails)
        {
            int updater = Interlocked.CompareExchange(ref detailUpdater, 1, 0);
            (SelectedActivity as ExploreActivity)?.UpdateActivity(SelectedStartTime, (SeasonType)SelectedStartSeason, (WeatherType)SelectedStartWeather, SelectedConsist, SelectedPath);
            if (updater == 0)
            {
                if (updateDetails)
                    ShowDetails();
                detailUpdater = 0;
            }
        }
        #endregion

        #region Consist lists
        private async Task LoadLocomotiveListAsync()
        {
            lock (consists)
            {
                if (ctsConsistLoading != null && !ctsConsistLoading.IsCancellationRequested)
                    ctsConsistLoading.Cancel();
                ctsConsistLoading = ResetCancellationTokenSource(ctsConsistLoading);
            }

            Folder selectedFolder = SelectedFolder;
            try
            {
                consists = (await Consist.GetConsists(selectedFolder, ctsConsistLoading.Token)).OrderBy(c => c.Name);
            }
            catch (TaskCanceledException)
            {
                consists = new Consist[0];
            }
            ShowLocomotiveList();
        }

        private void ShowLocomotiveList()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxLocomotive.BeginUpdate();
                    comboBoxLocomotive.Items.Clear();
                    comboBoxLocomotive.Items.Add(Locomotive.GetLocomotive(null));
                    comboBoxLocomotive.Items.AddRange(consists.Where(c => c.Locomotive != null).Select(c => c.Locomotive).Distinct().OrderBy(l => l.Name).ToArray());
                    if (comboBoxLocomotive.Items.Count == 1)
                        comboBoxLocomotive.Items.Clear();
                }
                finally
                {
                    comboBoxLocomotive.EndUpdate();
                }
                UpdateFromMenuSelection<Locomotive>(comboBoxLocomotive, Menu_SelectionIndex.Locomotive, l => l.FilePath);
            }
            else
            {
                try
                {
                    comboBoxLocomotive.BeginUpdate();
                    comboBoxConsist.BeginUpdate();
                    var consist = SelectedActivity.Consist;
                    comboBoxLocomotive.Items.Clear();
                    comboBoxLocomotive.Items.Add(consist.Locomotive);
                    comboBoxLocomotive.SelectedIndex = 0;
                    comboBoxConsist.Items.Clear();
                    comboBoxConsist.Items.Add(consist);
                    comboBoxConsist.SelectedIndex = 0;
                }
                finally
                {
                    comboBoxLocomotive.EndUpdate();
                    comboBoxConsist.EndUpdate();
                }
            }
            UpdateEnabled();
        }

        private void ShowConsistList()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxConsist.BeginUpdate();
                    comboBoxConsist.Items.Clear();
                    comboBoxConsist.Items.AddRange(consists.Where(c => comboBoxLocomotive.SelectedItem.Equals(c.Locomotive)).OrderBy(c => c.Name).ToArray());
                }
                finally
                {
                    comboBoxConsist.EndUpdate();
                }
                UpdateFromMenuSelection<Consist>(comboBoxConsist, Menu_SelectionIndex.Consist, c => c.FilePath);
            }
            UpdateEnabled();
        }
        #endregion

        #region Path lists
        private async Task LoadStartAtListAsync()
        {
            lock (paths)
            {
                if (ctsPathLoading != null && !ctsPathLoading.IsCancellationRequested)
                    ctsPathLoading.Cancel();
                ctsPathLoading = ResetCancellationTokenSource(ctsPathLoading);
            }

            ShowStartAtList();
            ShowHeadToList();

            var selectedRoute = SelectedRoute;
            try
            {
                paths = (await Path.GetPaths(selectedRoute, false, ctsPathLoading.Token)).OrderBy(a => a.ToString());
            }
            catch (TaskCanceledException)
            {
                paths = new Path[0];
            }
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
                ShowStartAtList();
        }

        private void ShowStartAtList()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxStartAt.BeginUpdate();
                    comboBoxStartAt.Items.Clear();
                    comboBoxStartAt.Items.AddRange(paths.Select(p => p.Start).Distinct().OrderBy(s => s.ToString()).ToArray());
                }
                finally
                {
                    comboBoxStartAt.EndUpdate();
                }
                // Because this list is unique names, we have to do some extra work to select it.
                if (settings.Menu_Selection.Length >= (int)Menu_SelectionIndex.Path)
                {
                    string pathFilePath = settings.Menu_Selection[(int)Menu_SelectionIndex.Path];
                    Path path = paths.FirstOrDefault(p => p.FilePath == pathFilePath);
                    if (path != null)
                        SelectComboBoxItem<string>(comboBoxStartAt, s => s == path.Start);
                    else if (comboBoxStartAt.Items.Count > 0)
                        comboBoxStartAt.SelectedIndex = 0;
                }
            }
            else
            {
                try
                {
                    comboBoxStartAt.BeginUpdate();
                    comboBoxHeadTo.BeginUpdate();
                    Path path = SelectedActivity.Path;
                    comboBoxStartAt.Items.Clear();
                    comboBoxStartAt.Items.Add(path.Start);
                    comboBoxHeadTo.Items.Clear();
                    comboBoxHeadTo.Items.Add(path);
                }
                finally
                {
                    comboBoxStartAt.EndUpdate();
                    comboBoxHeadTo.EndUpdate();
                }
                comboBoxStartAt.SelectedIndex = 0;
                comboBoxHeadTo.SelectedIndex = 0;
            }
            UpdateEnabled();
        }

        private void ShowHeadToList()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxHeadTo.BeginUpdate();
                    comboBoxHeadTo.Items.Clear();
                    comboBoxHeadTo.Items.AddRange(paths.Where(p => p.Start == (string)comboBoxStartAt.SelectedItem).ToArray());
                }
                finally
                {
                    comboBoxHeadTo.EndUpdate();
                }
                UpdateFromMenuSelection<Path>(comboBoxHeadTo, Menu_SelectionIndex.Path, c => c.FilePath);
            }
            UpdateEnabled();
        }
        #endregion

        #region Environment
        private void ShowEnvironment()
        {
            if (SelectedActivity == null || SelectedActivity is ExploreActivity)
            {
                try
                {
                    comboBoxStartTime.BeginUpdate();
                    comboBoxDuration.BeginUpdate();
                    comboBoxStartTime.Items.Clear();
                    foreach (var hour in Enumerable.Range(0, 24))
                        comboBoxStartTime.Items.Add(String.Format("{0}:00", hour));
                    comboBoxDuration.Items.Clear();
                    comboBoxDuration.Items.Add("");
                }
                finally
                {
                    comboBoxStartTime.EndUpdate();
                    comboBoxDuration.EndUpdate();
                }

                UpdateFromMenuSelection(comboBoxStartTime, Menu_SelectionIndex.Time, "12:00");
                UpdateFromMenuSelection(comboBoxStartSeason, Menu_SelectionIndex.Season, s => s.Key.ToString(), new KeyedComboBoxItem(1, ""));
                UpdateFromMenuSelection(comboBoxStartWeather, Menu_SelectionIndex.Weather, w => w.Key.ToString(), new KeyedComboBoxItem(0, ""));
                comboBoxDifficulty.SelectedIndex = 3;
                comboBoxDuration.SelectedIndex = 0;
            }
            else
            {
                try
                {
                    comboBoxStartTime.BeginUpdate();
                    comboBoxDuration.BeginUpdate();

                    comboBoxStartTime.Items.Clear();
                    comboBoxStartTime.Items.Add(SelectedActivity.StartTime.FormattedStartTime());
                    comboBoxDuration.Items.Clear();
                    comboBoxDuration.Items.Add(SelectedActivity.Duration.FormattedDurationTime());
                }
                finally
                {
                    comboBoxStartTime.EndUpdate();
                    comboBoxDuration.EndUpdate();
                }
                comboBoxStartTime.SelectedIndex = 0;
                comboBoxStartSeason.SelectedIndex = (int)SelectedActivity.Season;
                comboBoxStartWeather.SelectedIndex = (int)SelectedActivity.Weather;
                comboBoxDifficulty.SelectedIndex = (int)SelectedActivity.Difficulty;
                comboBoxDuration.SelectedIndex = 0;
            }
        }
        #endregion

        #region Timetable Set list
        private async Task LoadTimetableSetListAsync()
        {
            lock (timetableSets)
            {
                if (ctsTimeTableLoading != null && !ctsTimeTableLoading.IsCancellationRequested)
                    ctsTimeTableLoading.Cancel();
                ctsTimeTableLoading = ResetCancellationTokenSource(ctsTimeTableLoading);
            }

            ShowTimetableSetList();

            var selectedFolder = SelectedFolder;
            var selectedRoute = SelectedRoute;
            try
            {
                timetableSets = (await TimetableInfo.GetTimetableInfo(selectedFolder, selectedRoute, ctsTimeTableLoading.Token)).OrderBy(tt => tt.Description);
                timetableWeatherFileSet = (await WeatherFileInfo.GetTimetableWeatherFiles(selectedFolder, selectedRoute, ctsTimeTableLoading.Token)).OrderBy(a => a.ToString());
            }
            catch (TaskCanceledException)
            {
                timetableSets = new TimetableInfo[0];
                timetableWeatherFileSet = new WeatherFileInfo[0];
            }
            ShowTimetableSetList();
            ShowTimetableWeatherSet();
        }

        private void ShowTimetableSetList()
        {
            try
            {
                comboBoxTimetableSet.BeginUpdate();
                comboBoxTimetableSet.Items.Clear();
                comboBoxTimetableSet.Items.AddRange(timetableSets.ToArray());
            }
            finally
            {
                comboBoxTimetableSet.EndUpdate();
            }
            UpdateFromMenuSelection<TimetableInfo>(comboBoxTimetableSet, Menu_SelectionIndex.TimetableSet, t => t.FileName);
            UpdateEnabled();
        }

        private void UpdateTimetableSet()
        {
            if (SelectedTimetableSet != null)
            {
                SelectedTimetableSet.Day = SelectedTimetableDay;
                SelectedTimetableSet.Season = SelectedStartSeason;
                SelectedTimetableSet.Weather = SelectedStartWeather;
            }
        }

        private void ShowTimetableWeatherSet()
        {
            comboBoxTimetableWeatherFile.Items.Clear();
            foreach (var weatherFile in timetableWeatherFileSet)
            {
                comboBoxTimetableWeatherFile.Items.Add(weatherFile);
                UpdateEnabled();
            }
        }

        void UpdateTimetableWeatherSet()
        {
            SelectedTimetableSet.WeatherFile = SelectedWeatherFile.GetFullName();
        }

        #endregion

        #region Timetable list
        private void ShowTimetableList()
        {
            if (null != SelectedTimetableSet)
            {
                try
                {
                    comboBoxTimetable.BeginUpdate();
                    comboBoxTimetable.Items.Clear();
                    comboBoxTimetable.Items.AddRange(SelectedTimetableSet.TimeTables.ToArray());
                }
                finally
                {
                    comboBoxTimetable.EndUpdate();
                }
                UpdateFromMenuSelection<TimetableFile>(comboBoxTimetable, Menu_SelectionIndex.Timetable, t => t.Description);
            }
            else
                comboBoxTimetable.Items.Clear();

            UpdateEnabled();
        }
        #endregion

        #region Timetable Train list
        private void ShowTimetableTrainList()
        {
            if (null != SelectedTimetableSet)
            {
                try
                {
                    comboBoxTimetableTrain.BeginUpdate();
                    comboBoxTimetableTrain.Items.Clear();

                    var trains = SelectedTimetableSet.TimeTables[comboBoxTimetable.SelectedIndex].Trains;
                    trains.Sort();
                    comboBoxTimetableTrain.Items.AddRange(trains.ToArray());
                }
                finally
                {
                    comboBoxTimetableTrain.EndUpdate();
                }
                UpdateFromMenuSelection<TrainInformation>(comboBoxTimetableTrain, Menu_SelectionIndex.Train, t => t.Column.ToString());
            }
            else
                comboBoxTimetableTrain.Items.Clear();

            UpdateEnabled();
        }
        #endregion

        #region Timetable environment
        private void ShowTimetableEnvironment()
        {
            UpdateFromMenuSelection(comboBoxTimetableDay, Menu_SelectionIndex.Day, d => d.Key.ToString(), new KeyedComboBoxItem(0, string.Empty));
            UpdateFromMenuSelection(comboBoxTimetableSeason, Menu_SelectionIndex.Season, s => s.Key.ToString(), new KeyedComboBoxItem(1, string.Empty));
            UpdateFromMenuSelection(comboBoxTimetableWeather, Menu_SelectionIndex.Weather, w => w.Key.ToString(), new KeyedComboBoxItem(0, string.Empty));
        }
        #endregion

        #region Details
        private void ShowDetails()
        {
            ClearDetails();
            if (SelectedRoute != null && SelectedRoute.Description != null)
                AddDetailToShow(catalog.GetStringFmt("Route: {0}", SelectedRoute.Name), SelectedRoute.Description);

            if (radioButtonModeActivity.Checked)
            {
                if (SelectedConsist?.Locomotive?.Description != null)
                {
                    AddDetailToShow(catalog.GetStringFmt("Locomotive: {0}", SelectedConsist.Locomotive.Name), SelectedConsist.Locomotive.Description);
                }
                if (SelectedActivity?.Description != null)
                {
                    AddDetailToShow(catalog.GetStringFmt("Activity: {0}", SelectedActivity.Name), SelectedActivity.Description);
                    AddDetailToShow(catalog.GetString("Activity Briefing"), SelectedActivity.Briefing);
                }
                else if (SelectedPath != null)
                {
                    AddDetailToShow(catalog.GetStringFmt("Path: {0}", SelectedPath.Name),
                        string.Join("\n", catalog.GetStringFmt("Starting at: {0}", SelectedPath.Start),
                    catalog.GetStringFmt("Heading to: {0}", SelectedPath.End)));
                }
            }
            if (radioButtonModeTimetable.Checked)
            {
                if (SelectedTimetableSet != null)
                {
                    AddDetailToShow(catalog.GetStringFmt("Timetable set: {0}", SelectedTimetableSet), string.Empty);
                }
                if (SelectedTimetable != null)
                {
                    AddDetailToShow(catalog.GetStringFmt("Timetable: {0}", SelectedTimetable), string.Empty);
                }
                if (SelectedTimetableTrain != null)
                {
                    AddDetailToShow(catalog.GetStringFmt("Train: {0}", SelectedTimetableTrain), catalog.GetStringFmt("Start time: {0}", SelectedTimetableTrain.StartTime));
                    if (SelectedTimetableConsist != null)
                    {
                        AddDetailToShow(catalog.GetStringFmt("Consist: {0}", SelectedTimetableConsist.Name), string.Empty);
                        if (SelectedTimetableConsist.Locomotive != null && SelectedTimetableConsist.Locomotive.Description != null)
                        {
                            AddDetailToShow(catalog.GetStringFmt("Locomotive: {0}", SelectedTimetableConsist.Locomotive.Name), SelectedTimetableConsist.Locomotive.Description);
                        }
                    }
                    if (SelectedTimetablePath != null)
                    {
                        AddDetailToShow(catalog.GetStringFmt("Path: {0}", SelectedTimetablePath.Name), SelectedTimetablePath.ToInfo());
                    }
                }
            }

            FlowDetails();
        }

        private List<Detail> Details = new List<Detail>();

        private class Detail
        {
            public readonly Control Title;
            public readonly Control Expander;
            public readonly Control Summary;
            public readonly Control Description;
            public bool Expanded;
            public Detail(Control title, Control expander, Control summary, Control lines)
            {
                Title = title;
                Expander = expander;
                Summary = summary;
                Description = lines;
                Expanded = false;
            }
        }

        private void ClearDetails()
        {
            Details.Clear();
            while (panelDetails.Controls.Count > 0)
                panelDetails.Controls.RemoveAt(0);
        }

        private void AddDetailToShow(string title, string text)
        {
            panelDetails.SuspendLayout();
            var titleControl = new Label { Margin = new Padding(2), Text = title, UseMnemonic = false, Font = new Font(panelDetails.Font, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
            titleControl.Left = titleControl.Margin.Left;
            titleControl.Width = panelDetails.ClientSize.Width - titleControl.Margin.Horizontal - titleControl.PreferredHeight;
            titleControl.Height = titleControl.PreferredHeight;
            titleControl.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            panelDetails.Controls.Add(titleControl);

            var expanderControl = new Button { Margin = new Padding(0), Text = "", FlatStyle = FlatStyle.Flat };
            expanderControl.Left = panelDetails.ClientSize.Width - titleControl.Height - titleControl.Margin.Right;
            expanderControl.Width = expanderControl.Height = titleControl.Height;
            expanderControl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            expanderControl.FlatAppearance.BorderSize = 0;
            expanderControl.BackgroundImageLayout = ImageLayout.Center;
            panelDetails.Controls.Add(expanderControl);

            var summaryControl = new Label { Margin = new Padding(2), Text = text, AutoSize = false, UseMnemonic = false, UseCompatibleTextRendering = false };
            summaryControl.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            summaryControl.Left = summaryControl.Margin.Left;
            summaryControl.Width = panelDetails.ClientSize.Width - summaryControl.Margin.Horizontal;
            summaryControl.Height = MeasureTextHeigth("1\n2\n3\n4\n5", panelDetails.Font, summaryControl.ClientSize);
            panelDetails.Controls.Add(summaryControl);

            // Find out where we need to cut the text to make the summary 5 lines long. Uses a binary search to find the cut point.
            int size = MeasureTextHeigth(text, panelDetails.Font, summaryControl.ClientSize);
            int height = size;
            if (size > summaryControl.Height)
            {
                StringBuilder builder = new StringBuilder(text);
                float index = summaryControl.Text.Length;
                float indexChunk = index;
                while (indexChunk > 0.5f || size > summaryControl.Height)
                {
                    if (indexChunk > 0.5f)
                        indexChunk /= 2;
                    if (size > summaryControl.Height)
                        index -= indexChunk;
                    else
                        index += indexChunk;
                    size = MeasureTextHeigth(builder.ToString(0, (int)index) + "...", panelDetails.Font, summaryControl.ClientSize);
                }
                for (int i = 0; i < 3; i++)
                    builder[(int)index++] = '.';
                summaryControl.Text = builder.ToString(0, (int)index);
            }

            var descriptionControl = new Label { Margin = new Padding(2), Text = text, AutoSize = false, UseMnemonic = false, UseCompatibleTextRendering = false };
            descriptionControl.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            descriptionControl.Left = descriptionControl.Margin.Left;
            descriptionControl.Width = panelDetails.ClientSize.Width - descriptionControl.Margin.Horizontal;
            descriptionControl.Height = height;
            panelDetails.Controls.Add(descriptionControl);

            // Enable the expander only if the full description is longer than the summary. Otherwise, disable the expander.
            expanderControl.Enabled = descriptionControl.Height > summaryControl.Height;
            if (expanderControl.Enabled)
            {
                expanderControl.BackgroundImage = (Image)resources.GetObject("ExpanderClosed");
                expanderControl.Tag = Details.Count;
                expanderControl.Click += new EventHandler(ExpanderControl_Click);
            }
            else
            {
                expanderControl.BackgroundImage = (Image)resources.GetObject("ExpanderClosedDisabled");
            }
            Details.Add(new Detail(titleControl, expanderControl, summaryControl, descriptionControl));
            panelDetails.ResumeLayout();

        }

        private static int MeasureTextHeigth(string text, Font font, Size clientSize)
        {
            return TextRenderer.MeasureText(text, font, clientSize, TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        }

        private void ExpanderControl_Click(object sender, EventArgs e)
        {
            var index = (int)(sender as Control).Tag;
            Details[index].Expanded = !Details[index].Expanded;
            Details[index].Expander.BackgroundImage = (Image)resources.GetObject(Details[index].Expanded ? "ExpanderOpen" : "ExpanderClosed");
            FlowDetails();
        }

        private void FlowDetails()
        {
            var scrollPosition = panelDetails.AutoScrollPosition.Y;
            panelDetails.AutoScrollPosition = Point.Empty;
            panelDetails.AutoScrollMinSize = new Size(0, panelDetails.ClientSize.Height + 1);

            var top = 0;
            foreach (var detail in Details)
            {
                top += detail.Title.Margin.Top;
                detail.Title.Top = detail.Expander.Top = top;
                top += detail.Title.Height + detail.Title.Margin.Bottom + detail.Description.Margin.Top;
                detail.Summary.Top = detail.Description.Top = top;
                detail.Summary.Visible = !detail.Expanded && detail.Expander.Enabled;
                detail.Description.Visible = !detail.Summary.Visible;
                if (detail.Description.Visible)
                    top += detail.Description.Height + detail.Description.Margin.Bottom;
                else
                    top += detail.Summary.Height + detail.Summary.Margin.Bottom;
            }

            if (panelDetails.AutoScrollMinSize.Height < top)
                panelDetails.AutoScrollMinSize = new Size(0, top);
            panelDetails.AutoScrollPosition = new Point(0, -scrollPosition);
        }
        #endregion

        #region Utility functions
        private void UpdateFromMenuSelection<T>(ComboBox comboBox, Menu_SelectionIndex index, T defaultValue)
        {
            UpdateFromMenuSelection<T>(comboBox, index, _ => _.ToString(), defaultValue);
        }

        private void UpdateFromMenuSelection<T>(ComboBox comboBox, Menu_SelectionIndex index, Func<T, string> map)
        {
            UpdateFromMenuSelection(comboBox, index, map, default);
        }

        private void UpdateFromMenuSelection<T>(ComboBox comboBox, Menu_SelectionIndex index, Func<T, string> map, T defaultValue)
        {
            if (settings.Menu_Selection.Length > (int)index && settings.Menu_Selection[(int)index] != "")
            {
                if (comboBox.DropDownStyle == ComboBoxStyle.DropDown)
                    comboBox.Text = settings.Menu_Selection[(int)index];
                else
                    SelectComboBoxItem<T>(comboBox, item => map(item) == settings.Menu_Selection[(int)index]);
            }
            else
            {
                if (comboBox.DropDownStyle == ComboBoxStyle.DropDown)
                    comboBox.Text = map(defaultValue);
                else if (defaultValue != null)
                    SelectComboBoxItem<T>(comboBox, item => map(item) == map(defaultValue));
                else if (comboBox.Items.Count > 0)
                    comboBox.SelectedIndex = 0;
            }
        }

        private void SelectComboBoxItem<T>(ComboBox comboBox, Func<T, bool> predicate)
        {
            if (comboBox.Items.Count == 0)
                return;

            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is T && predicate((T)comboBox.Items[i]))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
            comboBox.SelectedIndex = 0;
        }

        private class KeyedComboBoxItem
        {
            public readonly int Key;
            public readonly string Value;

            public override string ToString()
            {
                return Value;
            }

            public KeyedComboBoxItem(int key, string value)
            {
                Key = key;
                Value = value;
            }
        }
        #endregion

        #region Executable utils
        private enum ImageSubsystem
        {
            Unknown = 0,
            Native = 1,
            WindowsGui = 2,
            WindowsConsole = 3,
        }

        private static ImageSubsystem GetImageSubsystem(BinaryReader stream)
        {
            try
            {
                var baseOffset = stream.BaseStream.Position;

                // WORD IMAGE_DOS_HEADER.e_magic = 0x4D5A (MZ)
                stream.BaseStream.Seek(baseOffset + 0, SeekOrigin.Begin);
                var dosMagic = stream.ReadUInt16();
                if (dosMagic != 0x5A4D)
                    return ImageSubsystem.Unknown;

                // LONG IMAGE_DOS_HEADER.e_lfanew
                stream.BaseStream.Seek(baseOffset + 60, SeekOrigin.Begin);
                var ntHeaderOffset = stream.ReadUInt32();
                if (ntHeaderOffset == 0)
                    return ImageSubsystem.Unknown;

                // DWORD IMAGE_NT_HEADERS.Signature = 0x00004550 (PE..)
                stream.BaseStream.Seek(baseOffset + ntHeaderOffset, SeekOrigin.Begin);
                var ntMagic = stream.ReadUInt32();
                if (ntMagic != 0x00004550)
                    return ImageSubsystem.Unknown;

                // WORD IMAGE_OPTIONAL_HEADER.Magic = 0x010A (32bit header) or 0x020B (64bit header)
                stream.BaseStream.Seek(baseOffset + ntHeaderOffset + 24, SeekOrigin.Begin);
                var optionalMagic = stream.ReadUInt16();
                if (optionalMagic != 0x010B && optionalMagic != 0x020B)
                    return ImageSubsystem.Unknown;

                // WORD IMAGE_OPTIONAL_HEADER.Subsystem
                // Note: There might need to be an adjustment for ImageBase being ULONGLONG in the 64bit header though this doesn't actually seem to be true.
                stream.BaseStream.Seek(baseOffset + ntHeaderOffset + 92, SeekOrigin.Begin);
                var peSubsystem = stream.ReadUInt16();

                return (ImageSubsystem)peSubsystem;
            }
            catch (EndOfStreamException)
            {
                return ImageSubsystem.Unknown;
            }
        }
        #endregion

        private static CancellationTokenSource ResetCancellationTokenSource(CancellationTokenSource cts)
        {
            if (cts != null)
            {
                cts.Dispose();
            }
            // Create a new cancellation token source so that can cancel all the tokens again 
            return new CancellationTokenSource();
        }

        private void ComboBoxTimetable_EnabledChanged(object sender, EventArgs e)
        {
            //Debrief Eval TTActivity.
            if (!comboBoxTimetable.Enabled)
            {
                //comboBoxTimetable.Enabled == false then we erase comboBoxTimetable and comboBoxTimetableTrain data.
                if (comboBoxTimetable.Items.Count > 0)
                {
                    comboBoxTimetable.Items.Clear();
                    comboBoxTimetableTrain.Items.Clear();
                    buttonStart.Enabled = false;
                }
            }
            //TO DO: Debrief Eval TTActivity
        }
    }
}
