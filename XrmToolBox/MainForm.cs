﻿// PROJECT : XrmToolBox
// This project was developed by Tanguy Touzard
// CODEPLEX: http://xrmtoolbox.codeplex.com
// BLOG: http://mscrmtools.blogspot.com

using System.ComponentModel;
using System.Threading;
using McTools.Xrm.Connection;
using McTools.Xrm.Connection.WinForms;
using Microsoft.Xrm.Client;
using Microsoft.Xrm.Client.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using XrmToolBox.AppCode;
using XrmToolBox.Attributes;
using XrmToolBox.Forms;
using XrmToolBox.UserControls;

namespace XrmToolBox
{
    public sealed partial class MainForm : Form
    {
        #region Variables

        private FormHelper fHelper;

        private ConnectionManager cManager;

        private CrmConnectionStatusBar ccsb;

        private IOrganizationService service;

        private ConnectionDetail currentConnectionDetail;

        private PluginManager pManager;

        private Options currentOptions;

        private string currentReleaseNote;

        private Panel infoPanel;

        private string initialConnectionName;

        private string initialPluginName;

        #endregion Variables

        #region Constructor

        public MainForm(string[] args)
        {
            if (args.Length > 0)
            {
                this.initialConnectionName = ExtractSwitchValue("/connection:", ref args);
                this.initialPluginName = ExtractSwitchValue("/plugin:", ref args);
            }

            InitializeComponent();

            ProcessMenuItemsForPlugin();
            MouseWheel += (sender, e) => HomePageTab.Focus();

            currentOptions = Options.Load();
            Text = string.Format("{0} (v{1})", Text, Assembly.GetExecutingAssembly().GetName().Version);

            ManageConnectionControl();
        }

        #endregion Constructor

        #region Initialization methods

        private void ManageConnectionControl()
        {
            cManager = ConnectionManager.Instance;
            cManager.RequestPassword += (sender, e) => fHelper.RequestPassword(e.ConnectionDetail);
            cManager.StepChanged += (sender, e) => ccsb.SetMessage(e.CurrentStep);
            cManager.ConnectionSucceed += (sender, e) =>
            {
                Controls.Remove(infoPanel);
                if (infoPanel != null) infoPanel.Dispose();

                currentConnectionDetail = e.ConnectionDetail;
                service = e.OrganizationService;
                ccsb.SetConnectionStatus(true, e.ConnectionDetail);
                ccsb.SetMessage(string.Empty);

                if (e.Parameter != null)
                {
                    var control = e.Parameter as UserControl;
                    if (control != null)
                    {
                        var realUserControl = control;
                        DisplayPluginControl(realUserControl);
                    }
                    else if (e.Parameter.ToString() == "ApplyConnectionToTabs" && tabControl1.TabPages.Count > 1)
                    {
                        ApplyConnectionToTabs();
                    }
                    else
                    {
                        var args = e.Parameter as RequestConnectionEventArgs;
                        if (args != null)
                        {
                            var userControl = (UserControl)args.Control;

                            args.Control.UpdateConnection(e.OrganizationService, currentConnectionDetail, args.ActionName, args.Parameter);

                            userControl.Parent.Text = string.Format("{0} ({1})",
                                userControl.Parent.Text.Split(' ')[0],
                                e.ConnectionDetail.ConnectionName);
                        }
                    }
                }
                else if (tabControl1.TabPages.Count > 1)
                {
                    ApplyConnectionToTabs();
                }

                this.StartPluginWithConnection();
            };
            cManager.ConnectionFailed += (sender, e) =>
            {
                this.Invoke(new Action(() =>
                {
                    Controls.Remove(infoPanel);
                    if (infoPanel != null) infoPanel.Dispose();

                    MessageBox.Show(this, e.FailureReason, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    currentConnectionDetail = null;
                    service = null;
                    ccsb.SetConnectionStatus(false, null);
                    ccsb.SetMessage(e.FailureReason);

                    this.StartPluginWithConnection();
                }));
            };

            fHelper = new FormHelper(this);
            ccsb = new CrmConnectionStatusBar(fHelper) { Dock = DockStyle.Bottom };
            Controls.Add(ccsb);
        }

        private void StartPluginWithConnection()
        {
            if (!string.IsNullOrEmpty(this.initialConnectionName) && !string.IsNullOrEmpty(this.initialPluginName))
            {
                this.StartPluginWithoutConnection();

                // Resetting initial connection name
                this.initialConnectionName = string.Empty;
            }
        }

        private void StartPluginWithoutConnection()
        {
            if (!string.IsNullOrEmpty(this.initialPluginName) && this.pManager != null && this.pManager.PluginsControls != null)
            {
                var pluginControl = this.pManager.PluginsControls.FirstOrDefault(x => ((Type)x.Tag).GetTitle() == this.initialPluginName);
                if (pluginControl != null)
                {
                    this.Invoke(new Action(() =>
                    {
                        this.DisplayPluginControl(pluginControl);
                    }));
                }

                // Resetting initial plugin name
                this.initialPluginName = string.Empty;
            }
        }

        private Task LaunchVersionCheck()
        {
            return new Task(() =>
            {
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                var cvc = new GithubVersionChecker(currentVersion, "MsCrmTools", "XrmToolBox");

                cvc.Run();

                if (cvc.Cpi != null && !string.IsNullOrEmpty(cvc.Cpi.Version))
                {
                    if (currentOptions.LastUpdateCheck.Date != DateTime.Now.Date)
                    {
                        this.Invoke(new Action(() =>
                        {
                            var nvForm = new NewVersionForm(currentVersion, cvc.Cpi.Version, cvc.Cpi.Description, "MsCrmTools", "XrmToolBox");
                            nvForm.ShowDialog(this);
                        }));
                    }
                }

                currentOptions.LastUpdateCheck = DateTime.Now;
                currentOptions.Save();
            });
        }

        private Task LaunchWelcomeDialog()
        {
            return new Task(() =>
            {
                this.Invoke(new Action(() =>
                {
                    var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                    var blackScreen = new WelcomeDialog(version) { StartPosition = FormStartPosition.CenterScreen };
                    blackScreen.ShowDialog(this);
                }));
            });
        }

        private Task launchInitialConnection(ConnectionDetail connectionDetail)
        {
            return new Task(() =>
            {
                ConnectionManager.Instance.ConnectToServer(connectionDetail);
            });
        }

        #endregion Initialization methods

        #region Form events

        private async void MainForm_Load(object sender, EventArgs e)
        {
            this.Opacity = 0;

            pManager = new PluginManager();
            pManager.LoadPlugins();

            this.DisplayPlugins();

            var tasks = new List<Task>
            {
                this.LaunchWelcomeDialog(),
                this.LaunchVersionCheck()
            };

            if (!string.IsNullOrEmpty(this.initialConnectionName))
            {
                var connectionDetail = ConnectionManager.Instance.ConnectionsList.Connections.FirstOrDefault(x => x.ConnectionName == this.initialConnectionName); ;

                if (connectionDetail != null)
                {
                    // If initiall connection is present, connect to given sever is initiated.
                    // After connection try to open intial plugin will be attempted.
                    tasks.Add(this.launchInitialConnection(connectionDetail));
                }
                else
                {
                    // Connection detail was not found, so name provided was incorrect.
                    // But if name of the plugin is set, it should be started
                    if (!string.IsNullOrEmpty(this.initialPluginName))
                    {
                        this.StartPluginWithoutConnection();
                    }
                }
            }
            else if (!string.IsNullOrEmpty(this.initialPluginName))
            {
                // If there is no initial connection, but initial plugin is set, openning plugin
                this.StartPluginWithoutConnection();
            }
            
            tasks.ForEach(x => x.Start());
            
            await Task.WhenAll(tasks.ToArray());

            // Adapt size of current form
            if (currentOptions.Size.IsMaximized)
            {
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                currentOptions.Size.ApplyFormSize(this);
            }

            AdaptPluginControlSize();

            WebProxyHelper.ApplyProxy();

            this.Opacity = 100;
        }

        private void DisplayPlugins(object filter = null)
        {
            if (pManager.Plugins.Count == 0)
            {
                this.Invoke(new Action(() =>
                    {
                        this.pnlHelp.Visible = true;
                    }));
                
                return;
            }
            
            var top = 4;
            int lastWidth = HomePageTab.Width - 28;

            var filteredPlugins = (filter != null
                ? pManager.Plugins.Where(p 
                    => p.GetTitle().ToLower().Contains(filter.ToString().ToLower())
                    || p.GetCompany().ToLower().Contains(filter.ToString().ToLower()))
                : pManager.Plugins).ToList();

            if (currentOptions.DisplayMostUsedFirst)
            {
                foreach (var item in currentOptions.MostUsedList.OrderByDescending(i => i.Count).ThenBy(i=>i.Name))
                {
                    var plugin = filteredPlugins.FirstOrDefault(x => x.FullName == item.Name);
                    if (plugin != null && (currentOptions.HiddenPlugins == null || !currentOptions.HiddenPlugins.Contains(plugin.GetTitle())))
                    {
                        DisplayOnePlugin(plugin, ref top, lastWidth, item.Count);
                    }
                }

                foreach (var plugin in filteredPlugins.OrderBy(p => p.GetTitle()))
                {
                    if (currentOptions.MostUsedList.All(i => i.Name != plugin.FullName) && (currentOptions.HiddenPlugins == null || !currentOptions.HiddenPlugins.Contains(plugin.GetTitle())))
                    {
                        DisplayOnePlugin(plugin, ref top, lastWidth);
                    }
                }
            }
            else
            {
                foreach (var plugin in filteredPlugins.OrderBy(p => p.GetTitle()))
                {
                    if (currentOptions.HiddenPlugins == null || !currentOptions.HiddenPlugins.Contains(plugin.GetTitle()))
                    {
                        DisplayOnePlugin(plugin, ref top, lastWidth);
                    }
                }
            }

            this.Invoke(new Action(() =>
                {
                    HomePageTab.Controls.Clear();

                    foreach (UserControl ctrl in pManager.PluginsControls.Where(p => filteredPlugins.Contains(p.Tag)))
                    {
                        ctrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                        HomePageTab.Controls.Add(ctrl);
                    }

                    AdaptPluginControlSize();
                }));
         }

        private Image GetImage(Type plugin, bool small = false)
        {
            // Default logo (no-logo)
            var thisAssembly = Assembly.GetExecutingAssembly();
            var logoStream = thisAssembly.GetManifestResourceStream(small ? "XrmToolBox.Images.nologo32.png" : "XrmToolBox.Images.nologo.png");
            if (logoStream == null)
            {
                throw new Exception("Unable to find no-logo stream!");
            }

            var logo = Image.FromStream(logoStream);
            
            // Old method
            var pluginControl = (IMsCrmToolsPluginUserControl)PluginManager.CreateInstance(plugin.Assembly.Location, plugin.FullName);
            if(pluginControl.PluginLogo != null)
            logo = pluginControl.PluginLogo;

            // Replace by new method if available
            var b64 = AssemblyAttributeHelper.GetStringAttributeValue(plugin.Assembly, small ? "SmallBase64Image" : "BigBase64Image");
            if (b64.Length > 0)
            {
                var bytes = Convert.FromBase64String(b64);
                var ms = new MemoryStream(bytes, 0, bytes.Length);
                ms.Write(bytes, 0, bytes.Length);
                logo = Image.FromStream(ms);
                ms.Close();
            }

            return logo;
        }

        private void DisplayOnePlugin(Type plugin, ref int top, int width, int count = -1)
        {
            PluginModel pm;

            if (currentOptions.DisplayLargeIcons)
            {
                pm = this.CreateModel<LargePluginModel>(plugin, ref top, width, count);
            }
            else
            {
                pm = this.CreateModel<SmallPluginModel>(plugin, ref top, width, count);
            }
        }

        private PluginModel CreateModel<T>(Type plugin, ref int top, int width, int count) 
            where T : PluginModel
        {
            var pm = (T)this.pManager.PluginsControls.FirstOrDefault(t => (Type)t.Tag == plugin && t is T);

            if (pm == null)
            {
            var title = plugin.GetTitle();
            var desc = plugin.GetDescription();
            var author = plugin.GetCompany();
            var version = plugin.Assembly.GetName().Version.ToString();

            var backColor = AssemblyAttributeHelper.GetColor(plugin.Assembly, typeof(BackgroundColorAttribute));
            var primaryColor = AssemblyAttributeHelper.GetColor(plugin.Assembly, typeof(PrimaryFontColorAttribute));
            var secondaryColor = AssemblyAttributeHelper.GetColor(plugin.Assembly, typeof(SecondaryFontColorAttribute));

                var args = new Type[] 
            {
                    typeof(Image), 
                    typeof(string), 
                    typeof(string), 
                    typeof(string), 
                    typeof(string), 
                    typeof(Color), 
                    typeof(Color),
                    typeof(Color), 
                    typeof(int)
                };

                var vals = new object[]
                {
                    GetImage(plugin), 
                    title,
                    desc, 
                    author, 
                    version, 
                    backColor, 
                    primaryColor,
                    secondaryColor,
                    count
                };

                var ctor = typeof(T).GetConstructor(args);
                pm = (T)ctor.Invoke(vals);
                
                pm.Tag = plugin;
                    pm.Clicked += PluginClicked;

                this.pManager.PluginsControls.Add(pm);
                }

                var localTop = top;

                this.Invoke(new Action(() =>
                    {
                    pm.Left = 4;
                    pm.Top = localTop;
                    pm.Width = width;
                    }));
            top += pm.Height + 4;

            return pm;
        }

        void MainForm_MessageBroker(object sender, MessageBusEventArgs message)
        {
            if (!IsMessageValid(sender, message))
            {
                return;
            }

            // Trying to find the tab where plugin is located
            var tab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Controls[0].GetType().GetTitle() == message.TargetPlugin);

            if (tab != null && !message.NewInstance)
            {
                // Using existing plugin instance, switching to plugin tab
                tabControl1.SelectTab(tabControl1.TabPages.IndexOf(tab));
            }
            else
            {
                // Searching for suitable plugin
                var targetModel = pManager.PluginsControls.FirstOrDefault(x => ((Type)x.Tag).GetTitle() == message.TargetPlugin);
                if (targetModel == null)
                {
                    throw new PluginNotFoundException("Plugin {0} was not found", message.TargetPlugin);
                }
                // Displaying plugin and keeping number of the tab where it was opened
                var tabIndex = DisplayPluginControl((UserControl)targetModel);
                // Getting the tab where plugin was opened
                tab = tabControl1.TabPages[tabIndex];
                // New intance of the plugin was created, even if user did not explicitly asked about this.
                message.NewInstance = true;
            }

            var targetControl = (UserControl)tab.Controls[0];

            if (targetControl is IMessageBusHost)
            {
                ((IMessageBusHost)targetControl).OnIncomingMessage(message);
            }
        }

        private bool IsMessageValid(object sender, MessageBusEventArgs message)
        {
            if (message == null || sender == null || !(sender is UserControl) || !(sender is IMsCrmToolsPluginUserControl))
            {
                // Error. Possible reasons are:
                // * empty sender 
                // * empty message
                // * sender is not UserControl
                // * sender is not XrmToolBox Plugin
                return false;
            }
            
            var sourceControl = (UserControl)sender;
            
            if (string.IsNullOrEmpty(message.SourcePlugin))
            {
                message.SourcePlugin = sourceControl.GetType().GetTitle();
            }
            else if (message.SourcePlugin != sourceControl.GetType().GetTitle())
            {
                // For some reason incorrect name was set in Source Plugin field
                return false;
            }
            
            // Everything went ok
            return true;
        }

        private void PluginClicked(object sender, EventArgs e)
        {
           
            if (service == null && MessageBox.Show(this, "Do you want to connect to an organization first?", "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                if(fHelper.AskForConnection(sender))
                    infoPanel = InformationPanel.GetInformationPanel(this, "Connecting...", 340, 120);
            }
            else
            {
                DisplayPluginControl((UserControl)sender);
            }
        }

        private void TsbConnectClick(object sender, EventArgs e)
        {
            if (fHelper.AskForConnection("ApplyConnectionToTabs"))
            {
                infoPanel = InformationPanel.GetInformationPanel(this, "Connecting...", 340, 120);
            }
        }

        private void TsbAboutClick(object sender, EventArgs e)
        {
            var aForm = new AboutForm { StartPosition = FormStartPosition.CenterParent };
            aForm.ShowDialog();
        }
        
        #endregion Form events

        private int DisplayPluginControl(UserControl plugin)
        {
            var tabIndex = 0;

            try
            {
                var controlType = (Type) plugin.Tag;
                var pluginControl = (UserControl) PluginManager.CreateInstance(controlType.Assembly.Location, controlType.FullName);

                if (service != null)
                {
                    var clonedService = (OrganizationService)currentConnectionDetail.GetOrganizationService();
                    ((OrganizationServiceProxy)clonedService.InnerService).SdkClientVersion = currentConnectionDetail.OrganizationVersion;

                    ((IMsCrmToolsPluginUserControl) pluginControl).UpdateConnection(clonedService,
                        currentConnectionDetail);
                }

                if (pluginControl is IMessageBusHost)
                {
                    ((IMessageBusHost)pluginControl).OnOutgoingMessage += MainForm_MessageBroker;
                }

                ((IMsCrmToolsPluginUserControl) pluginControl).OnRequestConnection += MainForm_OnRequestConnection;
                ((IMsCrmToolsPluginUserControl) pluginControl).OnCloseTool += MainForm_OnCloseTool;

                string name = string.Format("{0} ({1})", pluginControl.GetType().GetTitle(),
                    currentConnectionDetail != null
                        ? currentConnectionDetail.ConnectionName
                        : "Not connected");

                var newTab = new TabPage(name);
                tabControl1.TabPages.Add(newTab);

                pluginControl.Dock = DockStyle.Fill;
                pluginControl.Width = newTab.Width;
                pluginControl.Height = newTab.Height;

                newTab.Controls.Add(pluginControl);

                tabIndex = tabControl1.TabPages.Count - 1;

                tabControl1.SelectTab(tabIndex);

                var pluginInOption =
                    currentOptions.MostUsedList.FirstOrDefault(i => i.Name == pluginControl.GetType().FullName);
                if (pluginInOption == null)
                {
                    pluginInOption = new PluginUseCount {Name = pluginControl.GetType().FullName, Count = 0};
                    currentOptions.MostUsedList.Add(pluginInOption);
                }

                pluginInOption.Count++;

                var p1 = plugin as SmallPluginModel;
                if (p1 != null)
                    p1.UpdateCount(pluginInOption.Count);
                else
                {
                    var p2 = plugin as LargePluginModel;
                    if (p2 != null)
                    {
                        p2.UpdateCount(pluginInOption.Count);
                    }
                }

                if (currentOptions.LastAdvertisementDisplay == new DateTime() ||
                    currentOptions.LastAdvertisementDisplay > DateTime.Now ||
                    currentOptions.LastAdvertisementDisplay.AddDays(7) < DateTime.Now)
                {
                    bool displayAdvertisement = true;
                    try
                    {
                        var assembly =
                            Assembly.LoadFile(new FileInfo(Assembly.GetExecutingAssembly().Location).Directory +
                                              "\\McTools.StopAdvertisement.dll");
                        if (assembly != null)
                        {
                            Type type = assembly.GetType("McTools.StopAdvertisement.LicenseManager");
                            if (type != null)
                            {
                                MethodInfo methodInfo = type.GetMethod("IsValid");
                                if (methodInfo != null)
                                {
                                    object classInstance = Activator.CreateInstance(type, null);

                                    if ((bool) methodInfo.Invoke(classInstance, null))
                                    {
                                        displayAdvertisement = false;
                                    }
                                }
                            }
                        }
                    }
                    catch (FileNotFoundException)
                    {
                    }

                    if (displayAdvertisement)
                    {
                        var sc = new SupportScreen(currentReleaseNote);
                        sc.ShowDialog(this);
                        currentOptions.LastAdvertisementDisplay = DateTime.Now;
                    }
                }

                currentOptions.Save();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "An error occured when trying to display this plugin: " + error.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return tabIndex;
        }

        private string ExtractSwitchValue(string key, ref string[] args)
        {
            var name = string.Empty;

            foreach (var arg in args)
            {
                if (arg.StartsWith(key))
                {
                    name = arg.Substring(key.Length);
                }
            }

            return name;
        }

        void MainForm_OnCloseTool(object sender, EventArgs e)
        {
            RequestCloseTab((TabPage)((UserControl)sender).Parent, new PluginCloseInfo(ToolBoxCloseReason.PluginRequest));
        }

        private void MainForm_OnRequestConnection(object sender, EventArgs e)
        {
            if (fHelper.AskForConnection(e))
            {
                infoPanel = InformationPanel.GetInformationPanel(this, "Connecting...", 340, 120);
            }
        }

        private void ApplyConnectionToTabs()
        {
            var tabs = tabControl1.TabPages.Cast<TabPage>().Where(tab => tab.TabIndex != 0).ToList();

            var tcu = new TabConnectionUpdater(tabs) { StartPosition = FormStartPosition.CenterParent };

            if (tcu.ShowDialog() == DialogResult.OK)
            {
                foreach (TabPage tab in tcu.SelectedTabs)
                {
                    tab.GetPlugin().UpdateConnection(service, currentConnectionDetail);

                    tab.Text = string.Format("{0} ({1})",
                                        tab.Controls[0].GetType().GetTitle(),
                                        currentConnectionDetail != null
                                            ? currentConnectionDetail.ConnectionName
                                            : "Not connected");
                }
            }
        }

        #region Close Tabs/Plugins

        private IEnumerable<TabPage> GetPluginPages()
        {
            for (var i = tabControl1.TabPages.Count - 1; i > 0; i--)
            {
                yield return tabControl1.TabPages[i];
            }
        }

        private void closeCurrentTabToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab.TabIndex != 0)
            {
                RequestCloseTab(tabControl1.TabPages[tabControl1.SelectedTab.TabIndex], new PluginCloseInfo(ToolBoxCloseReason.CloseCurrent));
            }
        }

        private void CloseAllTabsToolStripMenuItemClick(object sender, EventArgs e)
        {
            RequestCloseTabs(GetPluginPages(), new PluginCloseInfo(ToolBoxCloseReason.CloseAll));
        }

        private void CloseAllTabsExceptActiveToolStripMenuItemClick(object sender, EventArgs e)
        {
            RequestCloseTabs(GetPluginPages().Where(p => tabControl1.SelectedTab != p), new PluginCloseInfo(ToolBoxCloseReason.CloseAllExceptActive));
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Save current form size for future usage
            currentOptions.Size.CurrentSize = Size;
            currentOptions.Size.IsMaximized = (WindowState == FormWindowState.Maximized);
            currentOptions.Save();

            // Warn to close opened plugins
            var info = new PluginCloseInfo(e.CloseReason);
            RequestCloseTabs(GetPluginPages(), info);
            e.Cancel = info.Cancel;
        }

        private void tabControl1_MouseClick(object sender, MouseEventArgs e)
        {
            var tabControl = sender as TabControl;
            if (tabControl == null || e.Button != MouseButtons.Middle) { return; }

            var tabs = tabControl.TabPages;
            var tabPage = tabs.Cast<TabPage>()
                .Where((t, i) => tabControl.GetTabRect(i).Contains(e.Location))
                .FirstOrDefault();

            if (tabPage != null && tabControl1.TabPages[0] != tabPage)
            {
                RequestCloseTab(tabPage, new PluginCloseInfo(ToolBoxCloseReason.CloseMiddleClick));
            }
        }

        private void RequestCloseTabs(IEnumerable<TabPage> pages, PluginCloseInfo info)
        {
            var pagesList = pages.ToList();
            if ((info.FormReason != CloseReason.None ||
                info.ToolBoxReason == ToolBoxCloseReason.CloseAll ||
                info.ToolBoxReason == ToolBoxCloseReason.CloseAllExceptActive)
                && pagesList.Count > 0)
            {
                info.Cancel = MessageBox.Show(@"Are you sure you want to close " + pagesList.Count + @" tab(s)?", @"Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes;
                if (info.Cancel)
                {
                    return;
                }
            }

            foreach (var page in pagesList)
            {
                RequestCloseTab(page, info);
                if (info.Cancel) return;
            }
        }

        private void RequestCloseTab(TabPage page, PluginCloseInfo info)
        {
            var plugin = page.GetPlugin();
            plugin.ClosingPlugin(info);
            if (info.Cancel)
            {
                return;
            }
            CloseTab(page);
        }

        /// <summary>
        /// Only to be called from the RequestCloseTab
        /// </summary>
        /// <param name="page"></param>
        private void CloseTab(TabPage page)
        {
            tabControl1.TabPages.Remove(page);
            if (page.Controls.Count == 0)
            {
                return;
            }
            var plugin = page.Controls[0] as UserControl;
            if (plugin == null)
            {
                return;
            }

            plugin.Dispose();
        }

        #endregion // Close Tabs/Plugins

        #region CodePlex

        #region Active Plugin

        private string GetCodePlexUrl(string page)
        {
            var plugin = tabControl1.SelectedTab.GetCodePlexPlugin();
            return String.Format("http://{0}.codeplex.com/{1}", plugin.CodePlexUrlName, page);
        }

        private string GetGithubBaseUrl(string page)
        {
            var plugin = tabControl1.SelectedTab.GetGithubPlugin();
            return String.Format("https://github.com/{0}/{1}/{2}", plugin.UserName, plugin.RepositoryName, page);
        }

        private void TsbRatePluginClick(object sender, EventArgs e)
        {
            Process.Start(GetCodePlexUrl("Releases"));
        }

        private void TsbDiscussPluginClick(object sender, EventArgs e)
        {
            Process.Start(GetCodePlexUrl("Discussions"));
        }

        private void TsbReportBugPluginClick(object sender, EventArgs e)
        {
            Process.Start(GetCodePlexUrl("WorkItem/Create"));
        }

        private void discussionPluginToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(GetGithubBaseUrl("issues/new"));
        }

        #endregion // Active Plugin

        private void TsbDiscussClick(object sender, EventArgs e)
        {
            Process.Start("https://github.com/MscrmTools/XrmToolBox/issues/new");
        }

        #endregion // CodePlex

        private void donateInUSDollarsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Donate("EN", "USD", "tanguy92@hotmail.com", "Donation for MSCRM Tools - XrmToolBox");
        }

        private void donateInEuroToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Donate("EN", "EUR", "tanguy92@hotmail.com", "Donation for MSCRM Tools - XrmToolBox");
        }

        private void donateInGBPToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Donate("EN", "GBP", "tanguy92@hotmail.com", "Donation for MSCRM Tools - XrmToolBox");
        }

        private void donateDollarPluginMenuItem_Click(object sender, EventArgs e)
        {
            var plugin = tabControl1.SelectedTab.GetPaypalPlugin();
            Donate("EN","USD", plugin.EmailAccount, plugin.DonationDescription);
        }

        private void donateEuroPluginMenuItem_Click(object sender, EventArgs e)
        {
            var plugin = tabControl1.SelectedTab.GetPaypalPlugin();
            Donate("EN", "EUR", plugin.EmailAccount, plugin.DonationDescription);
        }

        private void donateGbpPluginMenuItem_Click(object sender, EventArgs e)
        {
            var plugin = tabControl1.SelectedTab.GetPaypalPlugin();
            Donate("EN", "GBP", plugin.EmailAccount, plugin.DonationDescription);
        }

        private void Donate(string language, string currency, string emailAccount, string description)
        {
            var url =
               string.Format(
                   "https://www.paypal.com/cgi-bin/webscr?cmd=_donations&business={0}&lc={1}&item_name={2}&currency_code={3}&bn=PP%2dDonationsBF",
                   emailAccount,
                   language,
                   HttpUtility.UrlEncode(description),
                   currency);

            Process.Start(url);
        }

        private void TsbOptionsClick(object sender, EventArgs e)
        {
            var oDialog = new OptionsDialog(currentOptions);
            if (oDialog.ShowDialog(this) == DialogResult.OK)
            {
                bool reinitDisplay = currentOptions.DisplayMostUsedFirst != oDialog.Option.DisplayMostUsedFirst
                                     || currentOptions.MostUsedList.Count != oDialog.Option.MostUsedList.Count
                                     || currentOptions.DisplayLargeIcons != oDialog.Option.DisplayLargeIcons
                                     || !oDialog.Option.HiddenPlugins.SequenceEqual(currentOptions.HiddenPlugins);

              currentOptions = oDialog.Option;

               if (reinitDisplay)
                {
                    pManager.PluginsControls.Clear();
                    tabControl1.SelectedIndex = 0;
                    DisplayPlugins(tstxtFilterPlugin.Text);
                    AdaptPluginControlSize();
                }
            }
        }

        private void tsbManageConnections_Click(object sender, EventArgs e)
        {
            fHelper.DisplayConnectionsList(this);
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            ProcessMenuItemsForPlugin();
        }

        private void ProcessMenuItemsForPlugin()
        {
            if (tabControl1.SelectedIndex == 0) // Home Screen
            {
                tstxtFilterPlugin.Enabled = true;
                CodePlexPluginMenuItem.Visible = false;
                GithubXrmToolBoxMenuItem.Visible = false;
                PaypalXrmToolBoxToolStripMenuItem.Visible = false;
                PayPalSelectedPluginToolStripMenuItem.Visible = false;
                AssignCodePlexMenuItems(tsbCodePlex.DropDownItems);
                AssignPayPalMenuItems(tsbDonate.DropDownItems);
                return;
            }
            
            // Disabling plugin search if not a home screen 
            tstxtFilterPlugin.Enabled = false;

            var paypalPlugin = tabControl1.SelectedTab.GetPaypalPlugin();
            if (paypalPlugin == null)
            {
                PaypalXrmToolBoxToolStripMenuItem.Visible = false;
                PayPalSelectedPluginToolStripMenuItem.Visible = false;
                AssignPayPalMenuItems(tsbDonate.DropDownItems);
            }
            else
            {
                PaypalXrmToolBoxToolStripMenuItem.Visible = true;
                PayPalSelectedPluginToolStripMenuItem.Visible = true;
                PayPalSelectedPluginToolStripMenuItem.Text = paypalPlugin.GetType().GetTitle();
                AssignPayPalMenuItems(PaypalXrmToolBoxToolStripMenuItem.DropDownItems);
            }

            var plugin = tabControl1.SelectedTab.GetCodePlexPlugin();
            if (plugin == null)
            {
                var githubPlugin = tabControl1.SelectedTab.GetGithubPlugin();

                if (githubPlugin == null)
                {
                    CodePlexPluginMenuItem.Visible = false;
                    GithubXrmToolBoxMenuItem.Visible = false;
                    githubPluginMenuItem.Visible = false;
                    AssignCodePlexMenuItems(tsbCodePlex.DropDownItems);
                }
                else
                {
                    CodePlexPluginMenuItem.Visible = false;
                    GithubXrmToolBoxMenuItem.Visible = true;
                    githubPluginMenuItem.Visible = true;
                    githubPluginMenuItem.Text = githubPlugin.GetType().GetTitle();
                    AssignCodePlexMenuItems(GithubXrmToolBoxMenuItem.DropDownItems);
                }
            }
            else
            {
                CodePlexPluginMenuItem.Visible = true;
                GithubXrmToolBoxMenuItem.Visible = true;
                githubPluginMenuItem.Visible = false;
                CodePlexPluginMenuItem.Text = plugin.GetType().GetTitle();
                AssignCodePlexMenuItems(GithubXrmToolBoxMenuItem.DropDownItems);
            }
        }

        private void AssignCodePlexMenuItems(ToolStripItemCollection dropDownItems)
        {
            dropDownItems.AddRange(new ToolStripItem[] {
                startADiscussionToolStripMenuItem});
        }

        private void AssignPayPalMenuItems(ToolStripItemCollection dropDownItems)
        {
            dropDownItems.AddRange(new ToolStripItem[]
            {
                donateInUSDollarsToolStripMenuItem,
                donateInEuroToolStripMenuItem,
                donateInGBPToolStripMenuItem
            });
        }


        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            AdaptPluginControlSize();
        }

        private void AdaptPluginControlSize()
        {
            if (GetVisibleScrollbars(HomePageTab) == ScrollBars.Vertical)
            {
                foreach (var ctrl in HomePageTab.Controls)
                {
                    if (ctrl is UserControl)
                    {
                        ((UserControl)ctrl).Width = HomePageTab.Width - 28;
                    }
                }
            }
            else
            {
                foreach (var ctrl in HomePageTab.Controls)
                {
                    if (ctrl is UserControl)
                    {
                        ((UserControl)ctrl).Width = HomePageTab.Width - 10;
                    }
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (tabControl1.SelectedIndex == 0)
            {
                // Focus on plugins filter box on Ctrl+F should work on home screen only
                if (keyData == (Keys.Control | Keys.F))
                {
                    tstxtFilterPlugin.Focus();

                    return true;
                }
            }
            else
            {
                // Close current plugin on Ctrl+Q, Ctrl+W and Ctrl+F4
                if (keyData == (Keys.Control | Keys.Q) ||
                    keyData == (Keys.Control | Keys.W) ||
                    keyData == (Keys.Control | Keys.F4))
                {
                    RequestCloseTab(tabControl1.TabPages[tabControl1.SelectedIndex], new PluginCloseInfo(ToolBoxCloseReason.CloseHotKey));

                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected static ScrollBars GetVisibleScrollbars(ScrollableControl ctl)
        {
            if (ctl.HorizontalScroll.Visible)
                return ctl.VerticalScroll.Visible ? ScrollBars.Both : ScrollBars.Horizontal;
            else
                return ctl.VerticalScroll.Visible ? ScrollBars.Vertical : ScrollBars.None;
        }

        private Thread dThread;

        private void tstxtFilterPlugin_TextChanged(object sender, EventArgs e)
        {
            if (dThread != null)
            {
                dThread.Abort();
            }

            dThread = new Thread(DisplayPlugins);
            dThread.Start(tstxtFilterPlugin.Text);
        }
    }

    public static class Extensions
    {
        public static IMsCrmToolsPluginUserControl GetPlugin(this TabPage page)
        {
            return (IMsCrmToolsPluginUserControl)page.Controls[0];
        }

        public static ICodePlexPlugin GetCodePlexPlugin(this TabPage page)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            return page.Controls[0] as ICodePlexPlugin;
        }

        public static IGitHubPlugin GetGithubPlugin(this TabPage page)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            return page.Controls[0] as IGitHubPlugin;
        }

        public static IPayPalPlugin GetPaypalPlugin(this TabPage page)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            return page.Controls[0] as IPayPalPlugin;
        }

        public static string GetTitle(this Type pluginType)
        {
            return ((AssemblyTitleAttribute) GetAssemblyAttribute(pluginType.Assembly, typeof (AssemblyTitleAttribute))).Title;
        }

        public static string GetDescription(this Type pluginType)
        {
            return ((AssemblyDescriptionAttribute)GetAssemblyAttribute(pluginType.Assembly, typeof(AssemblyDescriptionAttribute))).Description;
        }

        public static string GetCompany(this Type pluginType)
        {
            return ((AssemblyCompanyAttribute)GetAssemblyAttribute(pluginType.Assembly, typeof(AssemblyCompanyAttribute))).Company;
        }

        private static object GetAssemblyAttribute(Assembly assembly, Type attributeType)
        {
            return assembly.GetCustomAttributes(attributeType, true)[0];
        }
    }
}

