using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Net.Http;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Forms; // Required for NotifyIcon/Tray (System.Windows.Forms.dll)

[assembly: AssemblyTitle("OptiSqueeze")]
[assembly: AssemblyDescription("Lightweight Video Compressor & Encoder")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("@saad2134")]
[assembly: AssemblyProduct("OptiSqueeze")]
[assembly: AssemblyCopyright("Copyright © @saad2134 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace OptiSqueeze
{
    public class Program
    {
        public static string AppDir = AppDomain.CurrentDomain.BaseDirectory;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]))
            {
                AppDir = args[0];
            }
            var app = new System.Windows.Application();
            app.Run(new MainWindow());
        }
    }

    public class VideoFile
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public long SizeBytes { get; set; }
        public string Codec { get; set; }
        public double Duration { get; set; }
        public string Status { get; set; }
        public string StatusDetails { get; set; }
    }

    public class HistoryItem
    {
        public string FilePath { get; set; }
        public string OutputPath { get; set; }
        public string Date { get; set; }
        public string Status { get; set; }
        public long OriginalSize { get; set; }
        public long CompressedSize { get; set; }
        public long SavedBytes { get; set; }
        public double Duration { get; set; }
        public string Encoder { get; set; }
    }

    public class MainWindow : Window
    {
        // App State
        private List<VideoFile> queue = new List<VideoFile>();
        private bool isCompressing = false;
        private Process activeFFmpegProcess = null;
        private bool isCancelled = false;
        private string activeTab = "compressor";

        // Theme Settings
        private bool isDarkTheme = true;
        private SolidColorBrush brushBg;
        private SolidColorBrush brushSidebarBg;
        private SolidColorBrush brushCardBg;
        private SolidColorBrush brushBorder;
        private SolidColorBrush brushTextMain;
        private SolidColorBrush brushTextMuted;

        // Hardware capabilities
        private bool nvencAvailable = false;
        private bool qsvAvailable = false;
        private bool amfAvailable = false;

        // Tray Icon
        private NotifyIcon trayIcon = null;

        // Path definitions
        private static string appDir = Program.AppDir;
        private static string binDir = System.IO.Path.Combine(Program.AppDir, "bin");
        private static string ffmpegPath = System.IO.Path.Combine(System.IO.Path.Combine(Program.AppDir, "bin"), "ffmpeg.exe");
        private static string ffprobePath = System.IO.Path.Combine(System.IO.Path.Combine(Program.AppDir, "bin"), "ffprobe.exe");
        private static string historyPath = System.IO.Path.Combine(Program.AppDir, ".compress_history");

        // WPF UI Layout references
        private System.Windows.Controls.Grid sidebarGrid;
        private Border sidebarSeparator;
        private System.Windows.Controls.TextBlock brandText;
        private System.Windows.Controls.Button btnThemeToggle;

        private Border paneCompressor;
        private Border paneHistory;
        private Border paneSettings;
        private System.Windows.Controls.Grid overlayBinaries;

        // UI Tab Buttons
        private System.Windows.Controls.Button btnTabCompressor;
        private System.Windows.Controls.Button btnTabHistory;
        private System.Windows.Controls.Button btnTabSettings;

        // Workspace Controls
        private Border dropZone;
        private System.Windows.Controls.Grid queuePanel;
        private System.Windows.Controls.ListView queueListView;
        private System.Windows.Controls.TextBlock queueCountText;
        private System.Windows.Controls.TextBlock totalInputSizeText;
        private System.Windows.Controls.Button btnStart;
        private System.Windows.Controls.Button btnCancel;
        private System.Windows.Controls.Button btnClearQueue;
        
        private System.Windows.Controls.TextBox consoleLogs;
        private Border consoleBorder;
        private System.Windows.Controls.Button btnToggleConsole;
        private bool isConsoleExpanded = false;

        // Hardware Badges
        private Border badgeNvenc;
        private Border badgeQsv;
        private Border badgeAmf;

        // Settings variables
        private System.Windows.Controls.ComboBox comboEncoder;
        private System.Windows.Controls.Slider sliderCrf;
        private System.Windows.Controls.TextBlock textCrfVal;
        private System.Windows.Controls.ComboBox comboPreset;
        private System.Windows.Controls.ComboBox comboAudio;
        private System.Windows.Controls.CheckBox cbOverwriteOriginal;
        private System.Windows.Controls.CheckBox cbForceOverwrite;
        private System.Windows.Controls.CheckBox cbSkipHevc;
        private System.Windows.Controls.CheckBox cbUseHistory;

        // Dashboard statistics
        private System.Windows.Controls.TextBlock statsTotalFiles;
        private System.Windows.Controls.TextBlock statsTotalSaved;
        private System.Windows.Controls.TextBlock statsAvgRatio;
        private System.Windows.Controls.ListView historyListView;

        // Downloader Overlay
        private System.Windows.Controls.Grid binaryActions;
        private System.Windows.Controls.Grid binaryProgressPanel;
        private System.Windows.Controls.TextBlock binaryProgressText;
        private System.Windows.Controls.TextBlock binaryProgressPercent;
        private System.Windows.Controls.ProgressBar binaryProgressBar;

        public MainWindow()
        {
            this.Title = "OptiSqueeze - Video Compressor & Encoder";
            this.Width = 1100;
            this.Height = 800;
            this.MinWidth = 850;
            this.MinHeight = 650;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Setup Custom Icon
            string iconPath = System.IO.Path.Combine(appDir, "src", "icon.png");
            if (File.Exists(iconPath))
            {
                try { this.Icon = new BitmapImage(new Uri(iconPath)); } catch {}
            }

            // Initialize theme colors
            UpdateThemeBrushes();

            // Drag and drop events
            this.AllowDrop = true;
            this.DragEnter += MainWindow_DragEnter;
            this.DragOver += MainWindow_DragOver;
            this.Drop += MainWindow_Drop;
            this.Closing += MainWindow_Closing;

            // Build Layout
            BuildLayout();

            // Apply Theme
            ApplyTheme();

            // Check for binaries on background start
            Dispatcher.BeginInvoke(new Action(async () => {
                await CheckAndSetupBinaries();
            }), DispatcherPriority.Background);
        }

        #region Theme Application & Management
        private void UpdateThemeBrushes()
        {
            if (isDarkTheme)
            {
                brushBg = GetBrush("#080b11");
                brushSidebarBg = GetBrush("#0d121c");
                brushCardBg = GetBrush("rgba(22,30,47,0.7)");
                brushBorder = GetBrush("rgba(255,255,255,0.06)");
                brushTextMain = Brushes.White;
                brushTextMuted = GetBrush("#94a3b8");
            }
            else
            {
                brushBg = GetBrush("#f8fafc"); // Slate 50
                brushSidebarBg = GetBrush("#f1f5f9"); // Slate 100
                brushCardBg = GetBrush("#ffffff"); // Pure white
                brushBorder = GetBrush("rgba(15,23,42,0.08)"); // Subtle dark border
                brushTextMain = GetBrush("#0f172a"); // Slate 900
                brushTextMuted = GetBrush("#475569"); // Slate 600
            }
        }

        private void ApplyTheme()
        {
            UpdateThemeBrushes();

            // Apply to main window
            this.Background = brushBg;

            // Apply to sidebar layout
            sidebarGrid.Background = brushSidebarBg;
            sidebarSeparator.BorderBrush = brushBorder;

            // Update Tab button states
            SwitchTab(activeTab);

            // Update overlay downloader background
            overlayBinaries.Background = isDarkTheme ? GetBrush("rgba(5,7,11,0.96)") : GetBrush("rgba(241,245,249,0.96)");

            // Update badge themes
            if (badgeNvenc != null) SetBadgeActive(badgeNvenc, nvencAvailable);
            if (badgeQsv != null) SetBadgeActive(badgeQsv, qsvAvailable);
            if (badgeAmf != null) SetBadgeActive(badgeAmf, amfAvailable);

            // Traverse the tree recursively to update dynamic UI controls
            UpdateChildControls(this);

            // Style ListView Column Headers
            ApplyThemeToListViewHeaders();
        }

        private Style GetColumnHeaderStyle()
        {
            var headerStyle = new Style(typeof(GridViewColumnHeader));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, brushTextMuted));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, brushSidebarBg));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, brushBorder));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.FontSizeProperty, 12.0));

            var template = new ControlTemplate(typeof(GridViewColumnHeader));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));

            var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            cpFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cpFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Left);
            borderFactory.AppendChild(cpFactory);

            template.VisualTree = borderFactory;
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, template));

            return headerStyle;
        }

        private void ApplyThemeToListViewHeaders()
        {
            var style = GetColumnHeaderStyle();
            if (queueListView != null && queueListView.View is System.Windows.Controls.GridView)
            {
                ((System.Windows.Controls.GridView)queueListView.View).ColumnHeaderContainerStyle = style;
            }
            if (historyListView != null && historyListView.View is System.Windows.Controls.GridView)
            {
                ((System.Windows.Controls.GridView)historyListView.View).ColumnHeaderContainerStyle = style;
            }
        }

        private void UpdateChildControls(DependencyObject parent)
        {
            if (parent == null) return;
            foreach (var childObj in LogicalTreeHelper.GetChildren(parent))
            {
                if (childObj is DependencyObject)
                {
                    var child = (DependencyObject)childObj;

                    if (child is TextBlock)
                    {
                        var tb = (TextBlock)child;
                        // Keep brand header text color custom, as well as success/error colors
                        if (tb != brandText && tb.Foreground != GetBrush("#06b6d4") && tb.Foreground != GetBrush("#10b981") && tb.Foreground != GetBrush("#ef4444") && tb.Foreground != GetBrush("#7c3aed"))
                        {
                            if (tb.Tag != null && tb.Tag.ToString() == "muted")
                            {
                                tb.Foreground = brushTextMuted;
                            }
                            else
                            {
                                tb.Foreground = brushTextMain;
                            }
                        }
                    }
                    else if (child is Border)
                    {
                        var border = (Border)child;
                        if (border.Tag != null)
                        {
                            string tag = border.Tag.ToString();
                            if (tag == "card")
                            {
                                border.Background = brushCardBg;
                                border.BorderBrush = brushBorder;
                            }
                        }
                    }
                    else if (child is System.Windows.Controls.CheckBox)
                    {
                        var cb = (System.Windows.Controls.CheckBox)child;
                        cb.Foreground = brushTextMain;
                    }
                    else if (child is System.Windows.Controls.ListView)
                    {
                        var lv = (System.Windows.Controls.ListView)child;
                        lv.Background = brushSidebarBg;
                        lv.Foreground = brushTextMain;
                        lv.BorderBrush = brushBorder;
                    }
                    else if (child is System.Windows.Controls.TextBox)
                    {
                        var tb = (System.Windows.Controls.TextBox)child;
                        if (tb != consoleLogs)
                        {
                            tb.Background = brushCardBg;
                            tb.Foreground = brushTextMain;
                            tb.BorderBrush = brushBorder;
                        }
                    }
                    else if (child is System.Windows.Controls.Button)
                    {
                        var btn = (System.Windows.Controls.Button)child;
                        if (btn.Tag != null && btn.Tag.ToString() == "secondary")
                        {
                            btn.Background = isDarkTheme ? GetBrush("rgba(255,255,255,0.05)") : GetBrush("rgba(15,23,42,0.05)");
                            btn.BorderBrush = brushBorder;
                            btn.Foreground = brushTextMain;
                        }
                    }

                    // Recurse down logical tree
                    UpdateChildControls(child);
                }
            }
        }

        private void ToggleTheme(object sender, RoutedEventArgs e)
        {
            isDarkTheme = !isDarkTheme;
            btnThemeToggle.Content = isDarkTheme ? "Theme: Dark 🌙" : "Theme: Light ☀️";
            ApplyTheme();
        }
        #endregion

        #region UI Layout Builders
        private void BuildLayout()
        {
            var mainGrid = new System.Windows.Controls.Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            this.Content = mainGrid;

            // Sidebar Grid reference
            sidebarGrid = new System.Windows.Controls.Grid();
            System.Windows.Controls.Grid.SetColumn(sidebarGrid, 0);
            mainGrid.Children.Add(sidebarGrid);

            // Add thin separator line
            sidebarSeparator = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
            System.Windows.Controls.Grid.SetColumn(sidebarSeparator, 0);
            mainGrid.Children.Add(sidebarSeparator);

            // Sidebar Content Stack
            var sidebarStack = new StackPanel { Margin = new Thickness(16, 24, 16, 24) };
            sidebarGrid.Children.Add(sidebarStack);

            // Brand Label
            brandText = new TextBlock
            {
                Text = "OptiSqueeze",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = GetBrush("#06b6d4"),
                Margin = new Thickness(0, 0, 0, 36),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            };
            sidebarStack.Children.Add(brandText);

            // Navigation Items
            btnTabCompressor = CreateTabButton("Compressor", true);
            btnTabCompressor.Click += (s, e) => SwitchTab("compressor");
            sidebarStack.Children.Add(btnTabCompressor);

            btnTabHistory = CreateTabButton("Stats & History", false);
            btnTabHistory.Click += (s, e) => SwitchTab("history");
            sidebarStack.Children.Add(btnTabHistory);

            btnTabSettings = CreateTabButton("Settings", false);
            btnTabSettings.Click += (s, e) => SwitchTab("settings");
            sidebarStack.Children.Add(btnTabSettings);

            // Hardware Badges Footer
            var footerStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(16, 0, 16, 24)
            };
            sidebarGrid.Children.Add(footerStack);

            // Theme Toggle Button
            btnThemeToggle = CreateNormalButton("Theme: Dark 🌙", false);
            btnThemeToggle.Height = 36;
            btnThemeToggle.Margin = new Thickness(0, 0, 0, 24);
            btnThemeToggle.Click += ToggleTheme;
            footerStack.Children.Add(btnThemeToggle);

            var hwTitle = new TextBlock
            {
                Text = "HARDWARE ACCELERATION",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = GetBrush("#64748b"),
                Margin = new Thickness(0, 0, 0, 8),
                Tag = "muted"
            };
            footerStack.Children.Add(hwTitle);

            var badgeGrid = new System.Windows.Controls.Grid();
            badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerStack.Children.Add(badgeGrid);

            badgeNvenc = CreateHardwareBadge("NVENC");
            System.Windows.Controls.Grid.SetColumn(badgeNvenc, 0);
            badgeGrid.Children.Add(badgeNvenc);

            badgeQsv = CreateHardwareBadge("QSV");
            System.Windows.Controls.Grid.SetColumn(badgeQsv, 1);
            badgeGrid.Children.Add(badgeQsv);

            badgeAmf = CreateHardwareBadge("AMF");
            System.Windows.Controls.Grid.SetColumn(badgeAmf, 2);
            badgeGrid.Children.Add(badgeAmf);

            // Main Content Area Grid
            var contentArea = new System.Windows.Controls.Grid { Margin = new Thickness(36) };
            System.Windows.Controls.Grid.SetColumn(contentArea, 1);
            mainGrid.Children.Add(contentArea);

            // PANE 1: COMPRESSOR
            paneCompressor = BuildCompressorPane();
            contentArea.Children.Add(paneCompressor);

            // PANE 2: HISTORY
            paneHistory = BuildHistoryPane();
            paneHistory.Visibility = Visibility.Collapsed;
            contentArea.Children.Add(paneHistory);

            // PANE 3: SETTINGS
            paneSettings = BuildSettingsPane();
            paneSettings.Visibility = Visibility.Collapsed;
            contentArea.Children.Add(paneSettings);

            // OVERLAY: BINARY DOWNLOADER
            overlayBinaries = BuildBinaryOverlay();
            overlayBinaries.Visibility = Visibility.Collapsed;
            System.Windows.Controls.Grid.SetColumnSpan(overlayBinaries, 2);
            mainGrid.Children.Add(overlayBinaries);
        }

        private System.Windows.Controls.Button CreateTabButton(string label, bool isActive)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = label,
                Height = 44,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                Padding = new Thickness(16, 0, 0, 0)
            };

            // Modern Flat Button Style template
            var template = new ControlTemplate(typeof(System.Windows.Controls.Button));
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.Name = "border";
            factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
            factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderBrushProperty));
            factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderThicknessProperty));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Left);
            contentPresenter.SetValue(ContentPresenter.MarginProperty, new Thickness(12, 0, 0, 0));
            factory.AppendChild(contentPresenter);

            template.VisualTree = factory;
            btn.Template = template;

            return btn;
        }

        private Border CreateHardwareBadge(string text)
        {
            var border = new Border
            {
                Background = GetBrush("rgba(255,255,255,0.03)"),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(0, 4, 0, 4),
                Margin = new Thickness(2)
            };

            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = GetBrush("#475569"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            border.Child = textBlock;

            return border;
        }

        private void SetBadgeActive(Border badge, bool active)
        {
            var txt = (TextBlock)badge.Child;
            if (active)
            {
                badge.Background = GetBrush("rgba(16,185,129,0.1)");
                badge.BorderBrush = GetBrush("rgba(16,185,129,0.3)");
                txt.Foreground = GetBrush("#10b981");
            }
            else
            {
                badge.Background = isDarkTheme ? GetBrush("rgba(255,255,255,0.03)") : GetBrush("rgba(15,23,42,0.03)");
                badge.BorderBrush = Brushes.Transparent;
                txt.Foreground = brushTextMuted;
            }
        }

        private Border BuildCompressorPane()
        {
            var mainContainer = new Border();
            var mGrid = new System.Windows.Controls.Grid();
            mGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainContainer.Child = mGrid;

            // Header Section
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
            System.Windows.Controls.Grid.SetRow(headerStack, 0);
            mGrid.Children.Add(headerStack);

            var title = new TextBlock
            {
                Text = "Compress Videos",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            headerStack.Children.Add(title);

            var sub = new TextBlock
            {
                Text = "Convert videos to highly compressed H.265 (HEVC) format to reclaim disk space.",
                FontSize = 14,
                Tag = "muted"
            };
            headerStack.Children.Add(sub);

            // Workspace Grid (Dropzone / Queue list)
            var workspaceGrid = new System.Windows.Controls.Grid();
            System.Windows.Controls.Grid.SetRow(workspaceGrid, 1);
            mGrid.Children.Add(workspaceGrid);

            // Dropzone Border
            dropZone = new Border
            {
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(36),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = "card"
            };
            workspaceGrid.Children.Add(dropZone);

            var dzStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            dropZone.Child = dzStack;

            var dzTitle = new TextBlock
            {
                Text = "Drag & Drop Video Files or Folders Here",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            dzStack.Children.Add(dzTitle);

            var dzSub = new TextBlock
            {
                Text = "Supports MP4, MKV, AVI, MOV, FLV, and WebM formats",
                FontSize = 13,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 24),
                Tag = "muted"
            };
            dzStack.Children.Add(dzSub);

            var btnStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            dzStack.Children.Add(btnStack);

            var btnBrowseFiles = CreateNormalButton("Browse Files", true);
            btnBrowseFiles.Click += (s, e) => BrowseFiles();
            btnStack.Children.Add(btnBrowseFiles);

            var btnBrowseFolder = CreateNormalButton("Browse Folder", false);
            btnBrowseFolder.Margin = new Thickness(12, 0, 0, 0);
            btnBrowseFolder.Click += (s, e) => BrowseFolder();
            btnStack.Children.Add(btnBrowseFolder);

            // Queue Panel Grid
            queuePanel = new System.Windows.Controls.Grid { Visibility = Visibility.Collapsed };
            queuePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            queuePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            queuePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            workspaceGrid.Children.Add(queuePanel);

            // Queue Header
            var qHeader = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 12) };
            System.Windows.Controls.Grid.SetRow(qHeader, 0);
            queuePanel.Children.Add(qHeader);

            queueCountText = new TextBlock { Text = "0 Files Loaded", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            qHeader.Children.Add(queueCountText);

            btnClearQueue = new System.Windows.Controls.Button
            {
                Content = "Clear All",
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = GetBrush("#ef4444"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.Medium
            };
            btnClearQueue.Click += (s, e) => ClearQueue();
            qHeader.Children.Add(btnClearQueue);

            // Queue ListView
            queueListView = new System.Windows.Controls.ListView { Margin = new Thickness(0, 0, 0, 12) };
            System.Windows.Controls.Grid.SetRow(queueListView, 1);
            queuePanel.Children.Add(queueListView);

            // GridView inside ListView
            var gridView = new System.Windows.Controls.GridView();
            queueListView.View = gridView;

            gridView.Columns.Add(new GridViewColumn { Header = "File Name", Width = 300, DisplayMemberBinding = new System.Windows.Data.Binding("Name") });
            gridView.Columns.Add(new GridViewColumn { Header = "Format", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding("Codec") });
            gridView.Columns.Add(new GridViewColumn { Header = "Original Size", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("SizeBytes") { Converter = new BytesConverter() } });
            gridView.Columns.Add(new GridViewColumn { Header = "Duration", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding("Duration") { Converter = new DurationConverter() } });
            gridView.Columns.Add(new GridViewColumn { Header = "Status", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("Status") });
            gridView.Columns.Add(new GridViewColumn { Header = "Details", Width = 160, DisplayMemberBinding = new System.Windows.Data.Binding("StatusDetails") });

            // Queue Action Bar
            var actionBar = new System.Windows.Controls.Grid { Margin = new Thickness(0, 8, 0, 0) };
            System.Windows.Controls.Grid.SetRow(actionBar, 2);
            queuePanel.Children.Add(actionBar);

            var sizeStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actionBar.Children.Add(sizeStack);

            sizeStack.Children.Add(new TextBlock { Text = "Total Input Size: ", Tag = "muted" });
            totalInputSizeText = new TextBlock { Text = "0 MB", FontWeight = FontWeights.Bold, Foreground = GetBrush("#06b6d4") };
            sizeStack.Children.Add(totalInputSizeText);

            var actBtnStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            actionBar.Children.Add(actBtnStack);

            btnStart = CreateNormalButton("Start Compression", true);
            btnStart.Click += async (s, e) => await StartTranscodingSession();
            actBtnStack.Children.Add(btnStart);

            btnCancel = CreateNormalButton("Cancel Process", false);
            btnCancel.Background = GetBrush("rgba(239, 68, 68, 0.2)");
            btnCancel.BorderBrush = GetBrush("rgba(239, 68, 68, 0.4)");
            btnCancel.Foreground = GetBrush("#ef4444");
            btnCancel.Visibility = Visibility.Collapsed;
            btnCancel.Click += (s, e) => CancelTranscodingSession();
            actBtnStack.Children.Add(btnCancel);

            // Collapsible Terminal Console (Footer Row 2)
            var consoleGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 16, 0, 0) };
            System.Windows.Controls.Grid.SetRow(consoleGrid, 2);
            mGrid.Children.Add(consoleGrid);

            var cStack = new StackPanel();
            consoleGrid.Children.Add(cStack);

            btnToggleConsole = new System.Windows.Controls.Button
            {
                Content = "Show FFmpeg Process Logs",
                Height = 32,
                Background = GetBrush("#06090e"),
                BorderBrush = GetBrush("rgba(255,255,255,0.06)"),
                Foreground = GetBrush("#94a3b8"),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnToggleConsole.Click += ToggleConsole;
            cStack.Children.Add(btnToggleConsole);

            consoleBorder = new Border
            {
                Height = 0,
                BorderBrush = GetBrush("rgba(255,255,255,0.06)"),
                BorderThickness = new Thickness(1, 0, 1, 1),
                Background = GetBrush("#06090e")
            };
            cStack.Children.Add(consoleBorder);

            consoleLogs = new System.Windows.Controls.TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = GetBrush("#a7f3d0"),
                FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                FontSize = 11,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                Text = "Console logs waiting for transcoding tasks...\n"
            };
            consoleBorder.Child = consoleLogs;

            return mainContainer;
        }

        private Border BuildHistoryPane()
        {
            var mainContainer = new Border();
            var hGrid = new System.Windows.Controls.Grid();
            hGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            hGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            hGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainContainer.Child = hGrid;

            // Header Section
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
            System.Windows.Controls.Grid.SetRow(headerStack, 0);
            hGrid.Children.Add(headerStack);

            headerStack.Children.Add(new TextBlock
            {
                Text = "Dashboard & Analytics",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = "Performance statistics and session storage tracking.",
                FontSize = 14,
                Tag = "muted"
            });

            // Statistics Grid Row 1
            var statsGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 24) };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            System.Windows.Controls.Grid.SetRow(statsGrid, 1);
            hGrid.Children.Add(statsGrid);

            // Card 1: Files Compressed
            var card1 = CreateStatsCard("Files Compressed", "0", out statsTotalFiles);
            System.Windows.Controls.Grid.SetColumn(card1, 0);
            statsGrid.Children.Add(card1);

            // Card 2: Space Reclaimed
            var card2 = CreateStatsCard("Total Disk Space Saved", "0 MB", out statsTotalSaved);
            System.Windows.Controls.Grid.SetColumn(card2, 1);
            statsGrid.Children.Add(card2);

            // Card 3: Compression Ratio
            var card3 = CreateStatsCard("Average Shrink Ratio", "0%", out statsAvgRatio);
            System.Windows.Controls.Grid.SetColumn(card3, 2);
            statsGrid.Children.Add(card3);

            // History Log Panel Row 2
            var logGrid = new System.Windows.Controls.Grid();
            logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            System.Windows.Controls.Grid.SetRow(logGrid, 2);
            hGrid.Children.Add(logGrid);

            // Log header
            var logHead = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 12) };
            System.Windows.Controls.Grid.SetRow(logHead, 0);
            logGrid.Children.Add(logHead);

            logHead.Children.Add(new TextBlock
            {
                Text = "Completed Conversions Log",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var btnClearHistory = new System.Windows.Controls.Button
            {
                Content = "Clear Log DB",
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = GetBrush("#ef4444"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.Medium
            };
            btnClearHistory.Click += async (s, e) => await ClearHistoryDb();
            logHead.Children.Add(btnClearHistory);

            // ListView
            historyListView = new System.Windows.Controls.ListView();
            System.Windows.Controls.Grid.SetRow(historyListView, 1);
            logGrid.Children.Add(historyListView);

            var gridView = new System.Windows.Controls.GridView();
            historyListView.View = gridView;

            gridView.Columns.Add(new GridViewColumn { Header = "Original File", Width = 280, DisplayMemberBinding = new System.Windows.Data.Binding("FilePath") { Converter = new PathFilenameConverter() } });
            gridView.Columns.Add(new GridViewColumn { Header = "Original Size", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("OriginalSize") { Converter = new BytesConverter() } });
            gridView.Columns.Add(new GridViewColumn { Header = "Compressed Size", Width = 95, DisplayMemberBinding = new System.Windows.Data.Binding("CompressedSize") { Converter = new BytesConverter() } });
            gridView.Columns.Add(new GridViewColumn { Header = "Saved Size", Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding("SavedBytes") { Converter = new SpaceReclaimedConverter() } });
            gridView.Columns.Add(new GridViewColumn { Header = "Encoder", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("Encoder") });
            gridView.Columns.Add(new GridViewColumn { Header = "Transcode Date", Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding("Date") });

            return mainContainer;
        }

        private Border CreateStatsCard(string title, string defVal, out TextBlock valText)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                Margin = new Thickness(4),
                Tag = "card"
            };

            var stack = new StackPanel();
            b.Child = stack;

            var t = new TextBlock { Text = title, FontSize = 12, Margin = new Thickness(0, 0, 0, 6), Tag = "muted" };
            stack.Children.Add(t);

            valText = new TextBlock { Text = defVal, FontSize = 24, FontWeight = FontWeights.Bold };
            stack.Children.Add(valText);

            return b;
        }

        private Border BuildSettingsPane()
        {
            var mainContainer = new Border();
            var sGrid = new System.Windows.Controls.Grid();
            sGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainContainer.Child = sGrid;

            // Header Section
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
            System.Windows.Controls.Grid.SetRow(headerStack, 0);
            sGrid.Children.Add(headerStack);

            headerStack.Children.Add(new TextBlock
            {
                Text = "Compression Settings",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = "Tune encoder preferences, quality variables, and folder replacement scripts.",
                FontSize = 14,
                Tag = "muted"
            });

            // Settings Panel Stack
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            System.Windows.Controls.Grid.SetRow(scroll, 1);
            sGrid.Children.Add(scroll);

            var setStack = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            scroll.Content = setStack;

            // Group 1: Video Config
            var groupVideo = CreateSettingsGroup("Video Encoding Options");
            setStack.Children.Add(groupVideo);
            var gvStack = (StackPanel)groupVideo.Child;

            gvStack.Children.Add(CreateLabel("Preferred Video Encoder"));
            comboEncoder = CreateSettingsCombo(new[] {
                "Auto-detect (Fastest GPU Encoder)",
                "NVIDIA NVENC H.265 (Hardware Accelerated)",
                "Intel QuickSync (QSV) H.265 (Hardware Accelerated)",
                "AMD AMF H.265 (Hardware Accelerated)",
                "CPU x265 (Software fallback - Slowest, high efficiency)"
            });
            comboEncoder.SelectionChanged += (s, e) => {
                if (comboEncoder.SelectedIndex == 4) comboPreset.Visibility = Visibility.Visible;
                else comboPreset.Visibility = Visibility.Collapsed;
            };
            gvStack.Children.Add(comboEncoder);

            var sliderHeader = new System.Windows.Controls.Grid { Margin = new Thickness(0, 16, 0, 4) };
            gvStack.Children.Add(sliderHeader);
            sliderHeader.Children.Add(new TextBlock { Text = "Constant Quality Factor (CRF / CQ)", FontWeight = FontWeights.Medium });
            textCrfVal = new TextBlock { Text = "24", Foreground = GetBrush("#06b6d4"), FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            sliderHeader.Children.Add(textCrfVal);

            sliderCrf = new System.Windows.Controls.Slider
            {
                Minimum = 18,
                Maximum = 32,
                Value = 24,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Margin = new Thickness(0, 0, 0, 4)
            };
            sliderCrf.ValueChanged += (s, e) => { textCrfVal.Text = sliderCrf.Value.ToString(); };
            gvStack.Children.Add(sliderCrf);

            var sliderLabels = new System.Windows.Controls.Grid();
            gvStack.Children.Add(sliderLabels);
            sliderLabels.Children.Add(new TextBlock { Text = "18 (Highest Quality / Large)", FontSize = 10, Tag = "muted" });
            sliderLabels.Children.Add(new TextBlock { Text = "32 (Lowest Quality / Small)", FontSize = 10, Tag = "muted", HorizontalAlignment = System.Windows.HorizontalAlignment.Right });

            comboPreset = CreateSettingsCombo(new[] {
                "Ultrafast CPU Preset", "Superfast", "Veryfast", "Faster", "Fast", "Medium (Recommended balance)", "Slow"
            });
            comboPreset.SelectedIndex = 5;
            comboPreset.Visibility = Visibility.Collapsed;
            gvStack.Children.Add(comboPreset);

            // Group 2: Audio & Output Config
            var groupAudio = CreateSettingsGroup("Audio & Target Folder Rules");
            setStack.Children.Add(groupAudio);
            var gaStack = (StackPanel)groupAudio.Child;

            gaStack.Children.Add(CreateLabel("Audio Processing Mode"));
            comboAudio = CreateSettingsCombo(new[] {
                "Copy Audio Track (Natively copies streams, 0% quality loss)",
                "Compress to AAC (128kbps stereo - saves minor space)"
            });
            gaStack.Children.Add(comboAudio);

            cbOverwriteOriginal = CreateSettingsCheckbox("Overwrite original file (Replaces original upon successful compression)", true);
            cbOverwriteOriginal.Margin = new Thickness(0, 16, 0, 0);
            gaStack.Children.Add(cbOverwriteOriginal);

            cbForceOverwrite = CreateSettingsCheckbox("Force overwrite/save output even if compressed size is larger than original", false);
            gaStack.Children.Add(cbForceOverwrite);

            // Group 3: Skips & History Config
            var groupFilters = CreateSettingsGroup("Exclusion Filters");
            setStack.Children.Add(groupFilters);
            var gfStack = (StackPanel)groupFilters.Child;

            cbSkipHevc = CreateSettingsCheckbox("Skip videos already encoded in modern codecs (HEVC/H.265, AV1, VP9)", true);
            gfStack.Children.Add(cbSkipHevc);

            cbUseHistory = CreateSettingsCheckbox("Skip files matching historical success database", true);
            gfStack.Children.Add(cbUseHistory);

            return mainContainer;
        }

        private Border CreateSettingsGroup(string title)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                Margin = new Thickness(0, 0, 0, 20),
                Tag = "card"
            };

            var stack = new StackPanel();
            b.Child = stack;

            var head = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = GetBrush("#06b6d4"),
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(head);

            return b;
        }

        private TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private System.Windows.Controls.ComboBox CreateSettingsCombo(string[] items)
        {
            var cb = new System.Windows.Controls.ComboBox
            {
                Height = 36,
                Background = GetBrush("#111724"),
                Foreground = Brushes.White,
                BorderBrush = GetBrush("rgba(255,255,255,0.1)"),
                Margin = new Thickness(0, 0, 0, 16),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0)
            };

            foreach (var item in items)
            {
                cb.Items.Add(new ComboBoxItem { Content = item, Foreground = Brushes.Black });
            }
            cb.SelectedIndex = 0;
            return cb;
        }

        private System.Windows.Controls.CheckBox CreateSettingsCheckbox(string label, bool defVal)
        {
            return new System.Windows.Controls.CheckBox
            {
                Content = label,
                IsChecked = defVal,
                Margin = new Thickness(0, 8, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private System.Windows.Controls.Button CreateNormalButton(string text, bool isPrimary)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = text,
                MinHeight = 38,
                Padding = new Thickness(24, 8, 24, 8), // Clean horizontal padding
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = isPrimary ? Brushes.White : Brushes.White, // overwritten dynamic brush in walker
                BorderThickness = new Thickness(1),
                Background = isPrimary ? GetBrush("rgba(124,58,237,0.85)") : GetBrush("rgba(255,255,255,0.05)"),
                BorderBrush = isPrimary ? GetBrush("rgba(124,58,237,0.4)") : GetBrush("rgba(255,255,255,0.1)"),
                Tag = isPrimary ? "primary" : "secondary"
            };

            // Custom Button template for flat UI supporting padding
            var template = new ControlTemplate(typeof(System.Windows.Controls.Button));
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.Name = "border";
            factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
            factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderBrushProperty));
            factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderThicknessProperty));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty)); // Binds control padding!

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            factory.AppendChild(contentPresenter);

            template.VisualTree = factory;
            btn.Template = template;

            return btn;
        }

        private System.Windows.Controls.Grid BuildBinaryOverlay()
        {
            var grid = new System.Windows.Controls.Grid();

            var stack = new StackPanel
            {
                Width = 480,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            grid.Children.Add(stack);

            var title = new TextBlock
            {
                Text = "FFmpeg Binaries Missing",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(title);

            var info = new TextBlock
            {
                Text = "OptiSqueeze requires FFmpeg & FFprobe to transcode and query video codec formats. These components are missing from your project binaries directory.",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                LineHeight = 20,
                Margin = new Thickness(0, 0, 0, 24),
                Tag = "muted"
            };
            stack.Children.Add(info);

            binaryActions = new System.Windows.Controls.Grid();
            stack.Children.Add(binaryActions);

            var btnDL = CreateNormalButton("Download & Setup Automatically (80MB)", true);
            btnDL.Height = 44;
            btnDL.Click += async (s, e) => await DownloadBinariesAsync();
            binaryActions.Children.Add(btnDL);

            binaryProgressPanel = new System.Windows.Controls.Grid { Visibility = Visibility.Collapsed };
            stack.Children.Add(binaryProgressPanel);

            var dlStack = new StackPanel();
            binaryProgressPanel.Children.Add(dlStack);

            var infoGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 8) };
            dlStack.Children.Add(infoGrid);

            binaryProgressText = new TextBlock { Text = "Downloading FFmpeg ZIP...", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
            infoGrid.Children.Add(binaryProgressText);

            binaryProgressPercent = new TextBlock { Text = "0%", Foreground = GetBrush("#06b6d4"), FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            infoGrid.Children.Add(binaryProgressPercent);

            binaryProgressBar = new System.Windows.Controls.ProgressBar
            {
                Height = 8,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Background = GetBrush("rgba(255,255,255,0.05)"),
                Foreground = GetBrush("rgba(6,182,212,0.85)"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            dlStack.Children.Add(binaryProgressBar);

            dlStack.Children.Add(new TextBlock
            {
                Text = "This will download the official GyanD FFmpeg essentials release and extract the executable files.",
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                Tag = "muted"
            });

            return grid;
        }
        #endregion

        #region Tab Navigation Actions
        private void SwitchTab(string target)
        {
            if (isCompressing && target != "compressor")
            {
                System.Windows.MessageBox.Show("Please wait for the current compression job to complete or cancel it first.", "Active Task Running", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            activeTab = target;

            // Reset buttons
            SetTabBtnState(btnTabCompressor, target == "compressor");
            SetTabBtnState(btnTabHistory, target == "history");
            SetTabBtnState(btnTabSettings, target == "settings");

            // Toggle panes
            paneCompressor.Visibility = target == "compressor" ? Visibility.Visible : Visibility.Collapsed;
            paneHistory.Visibility = target == "history" ? Visibility.Visible : Visibility.Collapsed;
            paneSettings.Visibility = target == "settings" ? Visibility.Visible : Visibility.Collapsed;

            if (target == "history")
            {
                LoadHistoryAndStats();
            }
        }

        private void SetTabBtnState(System.Windows.Controls.Button btn, bool active)
        {
            if (active)
            {
                btn.Background = isDarkTheme ? GetBrush("rgba(124,58,237,0.12)") : GetBrush("rgba(124,58,237,0.08)");
                btn.BorderBrush = GetBrush("rgba(124,58,237,0.4)");
                btn.BorderThickness = new Thickness(1);
                btn.Foreground = isDarkTheme ? Brushes.White : GetBrush("#7c3aed");
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.BorderBrush = Brushes.Transparent;
                btn.BorderThickness = new Thickness(0);
                btn.Foreground = brushTextMuted;
            }
        }
        #endregion

        #region UI Event Handlers
        private void MainWindow_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = System.Windows.DragDropEffects.Copy;
        }

        private void MainWindow_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = System.Windows.DragDropEffects.Copy;
        }

        private void MainWindow_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (isCompressing) return;
            string[] paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (paths != null && paths.Length > 0)
            {
                ScanPaths(paths);
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (isCompressing)
            {
                // Prompt minimized configuration
                var choice = System.Windows.MessageBox.Show(
                    "OptiSqueeze is currently compressing videos.\n\n" +
                    "Would you like to minimize the app to the system tray and continue in the background?\n" +
                    "(Clicking NO will abort the task and exit completely; CANCEL keeps the window open)",
                    "Compression Active",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );

                if (choice == MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    SetupSystemTray();
                    this.Hide();
                }
                else if (choice == MessageBoxResult.No)
                {
                    // Kill & Exit
                    if (activeFFmpegProcess != null)
                    {
                        try { activeFFmpegProcess.Kill(); } catch {}
                    }
                    if (trayIcon != null) trayIcon.Dispose();
                }
                else
                {
                    e.Cancel = true;
                }
            }
            else
            {
                if (trayIcon != null) trayIcon.Dispose();
            }
        }
        #endregion

        #region Drag and Drop & Folder Scanning
        private void BrowseFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.flv;*.webm;*.ts;*.m4v|All Files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                ScanPaths(dialog.FileNames);
            }
        }

        private void BrowseFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    ScanPaths(new[] { dialog.SelectedPath });
                }
            }
        }

        private void ScanPaths(string[] paths)
        {
            this.Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var filesToScan = new List<string>();

                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        filesToScan.AddRange(Directory.GetFiles(path, "*.*", SearchOption.AllDirectories));
                    }
                    else if (File.Exists(path))
                    {
                        filesToScan.Add(path);
                    }
                }

                // Check codecs of files and add to queue
                foreach (var file in filesToScan)
                {
                    string ext = System.IO.Path.GetExtension(file).ToLower();
                    if (ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" || ext == ".flv" || ext == ".webm" || ext == ".ts" || ext == ".m4v")
                    {
                        // Prevent duplicates
                        if (queue.Exists(x => x.Path == file)) continue;

                        // Query ffprobe
                        string codec = "unknown";
                        double duration = 0;
                        try
                        {
                            codec = GetVideoCodec(file);
                            duration = GetVideoDuration(file);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("ffprobe read failed: " + ex.Message);
                        }

                        queue.Add(new VideoFile
                        {
                            Name = System.IO.Path.GetFileName(file),
                            Path = file,
                            SizeBytes = new FileInfo(file).Length,
                            Codec = codec,
                            Duration = duration,
                            Status = "Pending",
                            StatusDetails = "Waiting to start"
                        });
                    }
                }

                RenderQueueList();

            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to scan directory: " + ex.Message, "Error Scanning", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        private void RenderQueueList()
        {
            if (queue.Count == 0)
            {
                dropZone.Visibility = Visibility.Visible;
                queuePanel.Visibility = Visibility.Collapsed;
                return;
            }

            dropZone.Visibility = Visibility.Collapsed;
            queuePanel.Visibility = Visibility.Visible;

            queueCountText.Text = queue.Count + " File" + (queue.Count > 1 ? "s" : "") + " Loaded";
            
            long totalBytes = 0;
            foreach (var file in queue)
            {
                totalBytes += file.SizeBytes;
            }

            totalInputSizeText.Text = FormatBytes(totalBytes);
            
            queueListView.ItemsSource = null;
            queueListView.ItemsSource = queue;
        }

        private void ClearQueue()
        {
            if (isCompressing) return;
            queue.Clear();
            RenderQueueList();
            consoleLogs.Text = "Console logs waiting for transcoding tasks...\n";
        }
        #endregion

        #region FFmpeg Process Core Logic
        private string GetVideoCodec(string filePath)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = "-v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 \"" + filePath + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output.Trim();
        }

        private double GetVideoDuration(string filePath)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = "-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"" + filePath + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            double dur;
            if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dur))
            {
                return dur;
            }
            return 0;
        }

        private async Task CheckAndSetupBinaries()
        {
            if (!File.Exists(ffmpegPath) || !File.Exists(ffprobePath))
            {
                overlayBinaries.Visibility = Visibility.Visible;
            }
            else
            {
                overlayBinaries.Visibility = Visibility.Collapsed;
                await DetectHardwareEncoders();
                LoadHistoryAndStats();
            }
        }

        private async Task DetectHardwareEncoders()
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = "-encoders",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                string output = await p.StandardOutput.ReadToEndAsync();
                p.WaitForExit();

                if (output.Contains("hevc_nvenc")) nvencAvailable = true;
                if (output.Contains("hevc_qsv")) qsvAvailable = true;
                if (output.Contains("hevc_amf")) amfAvailable = true;

                SetBadgeActive(badgeNvenc, nvencAvailable);
                SetBadgeActive(badgeQsv, qsvAvailable);
                SetBadgeActive(badgeAmf, amfAvailable);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Encoder check failed: " + ex.Message);
            }
        }

        private async Task DownloadBinariesAsync()
        {
            binaryActions.Visibility = Visibility.Collapsed;
            binaryProgressPanel.Visibility = Visibility.Visible;

            string zipUrl = "https://github.com/GyanD/codexffmpeg/releases/download/6.1.1/ffmpeg-6.1.1-essentials_build.zip";
            string zipPath = System.IO.Path.Combine(binDir, "ffmpeg.zip");
            string tempExtractPath = System.IO.Path.Combine(binDir, "ffmpeg_temp");

            try
            {
                if (!Directory.Exists(binDir)) Directory.CreateDirectory(binDir);

                // Download zip
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    using (var response = await client.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        var totalBytes = response.Content.Headers.ContentLength ?? 0;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;
                            int read;
                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;
                                if (totalBytes > 0)
                                {
                                    int pct = (int)((totalRead * 100) / totalBytes);
                                    binaryProgressBar.Value = pct;
                                    binaryProgressPercent.Text = pct + "%";
                                }
                            }
                        }
                    }
                }

                // Extract Zip
                binaryProgressText.Text = "Extracting files (this may take a few seconds)...";
                binaryProgressBar.Value = 100;
                binaryProgressPercent.Text = "100%";

                await Task.Run(() => {
                    if (Directory.Exists(tempExtractPath)) Directory.Delete(tempExtractPath, true);
                    Directory.CreateDirectory(tempExtractPath);

                    ZipFile.ExtractToDirectory(zipPath, tempExtractPath);

                    // Locate executables recursively and copy
                    foreach (var file in Directory.GetFiles(tempExtractPath, "*.exe", SearchOption.AllDirectories))
                    {
                        string name = System.IO.Path.GetFileName(file);
                        if (name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(file, ffmpegPath, true);
                        }
                        else if (name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(file, ffprobePath, true);
                        }
                    }

                    // Clean up temp zip
                    File.Delete(zipPath);
                    Directory.Delete(tempExtractPath, true);
                });

                // Check again
                await CheckAndSetupBinaries();

            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("FFmpeg download failed: " + ex.Message, "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                binaryActions.Visibility = Visibility.Visible;
                binaryProgressPanel.Visibility = Visibility.Collapsed;
            }
        }
        #endregion

        #region Transcode Queue Execution
        private async Task StartTranscodingSession()
        {
            if (queue.Count == 0 || isCompressing) return;

            isCompressing = true;
            isCancelled = false;
            btnStart.Visibility = Visibility.Collapsed;
            btnCancel.Visibility = Visibility.Visible;
            btnClearQueue.IsEnabled = false;

            // Lock navigation
            btnTabHistory.IsEnabled = false;
            btnTabSettings.IsEnabled = false;
            comboEncoder.IsEnabled = false;
            sliderCrf.IsEnabled = false;
            comboPreset.IsEnabled = false;
            comboAudio.IsEnabled = false;
            cbOverwriteOriginal.IsEnabled = false;
            cbForceOverwrite.IsEnabled = false;
            cbSkipHevc.IsEnabled = false;
            cbUseHistory.IsEnabled = false;

            consoleLogs.Text = "Starting video compression session...\n";

            // Read settings
            int encoderIndex = comboEncoder.SelectedIndex;
            int crf = (int)sliderCrf.Value;
            string preset = "medium";
            if (comboPreset.SelectedIndex == 0) preset = "ultrafast";
            else if (comboPreset.SelectedIndex == 1) preset = "superfast";
            else if (comboPreset.SelectedIndex == 2) preset = "veryfast";
            else if (comboPreset.SelectedIndex == 3) preset = "faster";
            else if (comboPreset.SelectedIndex == 4) preset = "fast";
            else if (comboPreset.SelectedIndex == 5) preset = "medium";
            else if (comboPreset.SelectedIndex == 6) preset = "slow";

            bool copyAudio = comboAudio.SelectedIndex == 0;
            bool overwrite = cbOverwriteOriginal.IsChecked == true;
            bool force = cbForceOverwrite.IsChecked == true;
            bool skipHevc = cbSkipHevc.IsChecked == true;
            bool useHistory = cbUseHistory.IsChecked == true;

            var historyMap = LoadHistoryMap();
            long totalSavedSession = 0;

            for (int i = 0; i < queue.Count; i++)
            {
                if (isCancelled) break;

                var file = queue[i];
                file.Status = "Running";
                file.StatusDetails = "Initializing encoder...";
                queueListView.Items.Refresh();
                queueListView.ScrollIntoView(file);

                bool isAlreadyHevc = file.Codec.Equals("hevc", StringComparison.OrdinalIgnoreCase) ||
                                     file.Codec.Equals("av1", StringComparison.OrdinalIgnoreCase) ||
                                     file.Codec.Equals("vp9", StringComparison.OrdinalIgnoreCase);

                // Exclude Modern Codecs Filter
                if (skipHevc && isAlreadyHevc)
                {
                    file.Status = "Skipped";
                    file.StatusDetails = "Already H.265/HEVC or alternative efficient format.";
                    queueListView.Items.Refresh();

                    AddToHistoryDb(new HistoryItem
                    {
                        FilePath = file.Path,
                        Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                        Status = "Skipped",
                        OriginalSize = file.SizeBytes,
                        CompressedSize = file.SizeBytes,
                        SavedBytes = 0,
                        Duration = file.Duration,
                        Encoder = file.Codec
                    });
                    continue;
                }

                // History lookup filter
                if (useHistory && historyMap.ContainsKey(file.Path))
                {
                    file.Status = "Skipped";
                    file.StatusDetails = "Successfully compressed in history database.";
                    queueListView.Items.Refresh();
                    continue;
                }

                // Pick encoder
                string encoder = "libx265";
                if (encoderIndex == 0) // Auto-detect
                {
                    if (nvencAvailable) encoder = "hevc_nvenc";
                    else if (qsvAvailable) encoder = "hevc_qsv";
                    else if (amfAvailable) encoder = "hevc_amf";
                }
                else if (encoderIndex == 1) encoder = "hevc_nvenc";
                else if (encoderIndex == 2) encoder = "hevc_qsv";
                else if (encoderIndex == 3) encoder = "hevc_amf";

                // Build FFmpeg details
                string dir = System.IO.Path.GetDirectoryName(file.Path);
                string tempPath = System.IO.Path.Combine(dir, "temp_compress_" + System.IO.Path.GetFileName(file.Path));

                var argsBuilder = new StringBuilder();
                argsBuilder.Append("-y -v error -stats -i \"" + file.Path + "\" -c:v " + encoder + " ");

                if (encoder == "hevc_nvenc")
                {
                    argsBuilder.Append("-cq " + crf + " ");
                }
                else if (encoder == "hevc_qsv")
                {
                    argsBuilder.Append("-global_quality " + crf + " ");
                }
                else if (encoder == "hevc_amf")
                {
                    argsBuilder.Append("-rc cqp -qp_i " + crf + " -qp_p " + crf + " ");
                }
                else
                {
                    argsBuilder.Append("-crf " + crf + " -preset " + preset + " ");
                }

                if (copyAudio)
                {
                    argsBuilder.Append("-c:a copy ");
                }
                else
                {
                    argsBuilder.Append("-c:a aac -b:a 128k ");
                }

                argsBuilder.Append("-map_metadata 0 \"" + tempPath + "\"");

                AppendLog("Running: ffmpeg " + argsBuilder.ToString() + "\n");

                bool success = false;
                try
                {
                    success = await RunFFmpegWithProgress(argsBuilder.ToString(), file, i);
                }
                catch (Exception ex)
                {
                    AppendLog("FFmpeg execution crashed: " + ex.Message + "\n");
                }

                if (success && !isCancelled)
                {
                    if (File.Exists(tempPath))
                    {
                        long oldSize = file.SizeBytes;
                        long newSize = new FileInfo(tempPath).Length;

                        if (newSize < oldSize || force)
                        {
                            string targetPath = file.Path;
                            try
                            {
                                if (overwrite)
                                {
                                    File.Delete(file.Path);
                                    File.Move(tempPath, file.Path);
                                }
                                else
                                {
                                    string dirName = System.IO.Path.GetDirectoryName(file.Path);
                                    string ext = System.IO.Path.GetExtension(file.Path);
                                    string name = System.IO.Path.GetFileNameWithoutExtension(file.Path);
                                    targetPath = System.IO.Path.Combine(dirName, name + "_compressed" + ext);
                                    if (File.Exists(targetPath)) File.Delete(targetPath);
                                    File.Move(tempPath, targetPath);
                                }

                                long saved = oldSize - newSize;
                                totalSavedSession += saved;
                                int pct = (int)((saved * 100) / oldSize);

                                file.Status = "Completed";
                                file.StatusDetails = "Saved " + FormatBytes(saved) + " (-" + pct + "%)";
                                queueListView.Items.Refresh();

                                AddToHistoryDb(new HistoryItem
                                {
                                    FilePath = file.Path,
                                    OutputPath = targetPath,
                                    Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                                    Status = "Success",
                                    OriginalSize = oldSize,
                                    CompressedSize = newSize,
                                    SavedBytes = saved,
                                    Duration = file.Duration,
                                    Encoder = encoder
                                });

                            }
                            catch (Exception ex)
                            {
                                file.Status = "Failed";
                                file.StatusDetails = "File replace write error.";
                                queueListView.Items.Refresh();
                                AppendLog("Disk Write Error replacing files: " + ex.Message + "\n");
                                if (File.Exists(tempPath)) File.Delete(tempPath);
                            }
                        }
                        else
                        {
                            // Discard larger output file
                            File.Delete(tempPath);
                            file.Status = "Skipped";
                            file.StatusDetails = "Compressed file was larger than original.";
                            queueListView.Items.Refresh();

                            AddToHistoryDb(new HistoryItem
                            {
                                FilePath = file.Path,
                                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                                Status = "Skipped (Larger)",
                                OriginalSize = oldSize,
                                CompressedSize = oldSize,
                                SavedBytes = 0,
                                Duration = file.Duration,
                                Encoder = encoder
                            });
                        }
                    }
                    else
                    {
                        file.Status = "Failed";
                        file.StatusDetails = "Missing expected temp target output.";
                        queueListView.Items.Refresh();
                    }
                }
                else
                {
                    // Aborted or failed
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    file.Status = isCancelled ? "Cancelled" : "Failed";
                    file.StatusDetails = isCancelled ? "Cancelled by user." : "Conversion process failed.";
                    queueListView.Items.Refresh();
                }
            }

            // Session completed notification
            isCompressing = false;
            btnStart.Visibility = Visibility.Visible;
            btnCancel.Visibility = Visibility.Collapsed;
            btnClearQueue.IsEnabled = true;

            // Re-enable navigation
            btnTabHistory.IsEnabled = true;
            btnTabSettings.IsEnabled = true;
            comboEncoder.IsEnabled = true;
            sliderCrf.IsEnabled = true;
            comboPreset.IsEnabled = comboEncoder.SelectedIndex == 4;
            comboAudio.IsEnabled = true;
            cbOverwriteOriginal.IsEnabled = true;
            cbForceOverwrite.IsEnabled = true;
            cbSkipHevc.IsEnabled = true;
            cbUseHistory.IsEnabled = true;

            // Tray Notification if app hidden
            if (trayIcon != null && !this.IsVisible)
            {
                trayIcon.ShowBalloonTip(3000, "OptiSqueeze Tasks Complete", "Successfully compressed videos. Total space saved: " + FormatBytes(totalSavedSession), ToolTipIcon.Info);
            }

            System.Windows.MessageBox.Show(
                "Transcoding session complete!\n\nTotal space saved: " + FormatBytes(totalSavedSession),
                "Compressor Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private Task<bool> RunFFmpegWithProgress(string arguments, VideoFile file, int index)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            activeFFmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            activeFFmpegProcess.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                // Log raw output
                AppendLog(e.Data + "\n");

                // Parse stats: frame=  281 fps=140 q=-0.0 size=    2304kB time=00:00:11.24 bitrate=1678.9kbits/s speed=5.58x
                string line = e.Data.Trim();

                var timeMatch = Regex.Match(line, "time=(\\d{2}):(\\d{2}):(\\d{2})\\.(\\d{2})");
                var fpsMatch = Regex.Match(line, "fps=\\s*(\\d+(\\.\\d+)?)");
                var speedMatch = Regex.Match(line, "speed=\\s*(\\d+(\\.\\d+)?)x");

                if (timeMatch.Success && file.Duration > 0)
                {
                    int hrs = int.Parse(timeMatch.Groups[1].Value);
                    int mins = int.Parse(timeMatch.Groups[2].Value);
                    int secs = int.Parse(timeMatch.Groups[3].Value);
                    int ms = int.Parse(timeMatch.Groups[4].Value);

                    double currentSecs = (hrs * 3600) + (mins * 60) + secs + (ms / 100.0);
                    int pct = (int)Math.Min((currentSecs * 100) / file.Duration, 99);

                    string speed = speedMatch.Success ? speedMatch.Groups[0].Value : "0x";
                    double speedVal = 0;
                    if (speedMatch.Success) double.TryParse(speedMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out speedVal);

                    double fps = 0;
                    if (fpsMatch.Success) double.TryParse(fpsMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fps);

                    string eta = "Calculating...";
                    if (speedVal > 0 && pct > 5)
                    {
                        double remaining = (file.Duration - currentSecs) / speedVal;
                        int remMin = (int)(remaining / 60);
                        int remSec = (int)(remaining % 60);
                        eta = remMin + "m " + remSec + "s left";
                    }

                    Dispatcher.Invoke(() =>
                    {
                        file.Status = "Running (" + pct + "%)";
                        file.StatusDetails = speed + " | " + fps + " FPS | " + eta;
                        queueListView.Items.Refresh();
                    });
                }
            };

            activeFFmpegProcess.Exited += (sender, e) =>
            {
                int exitCode = activeFFmpegProcess.ExitCode;
                activeFFmpegProcess.Dispose();
                activeFFmpegProcess = null;
                tcs.SetResult(exitCode == 0);
            };

            activeFFmpegProcess.Start();
            activeFFmpegProcess.BeginErrorReadLine();

            return tcs.Task;
        }

        private void CancelTranscodingSession()
        {
            isCancelled = true;
            btnCancel.IsEnabled = false;
            btnCancel.Content = "Cancelling...";
            if (activeFFmpegProcess != null)
            {
                try
                {
                    activeFFmpegProcess.Kill();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Cancel process kill failed: " + ex.Message);
                }
            }
        }
        #endregion

        #region System Tray Controls
        private void SetupSystemTray()
        {
            if (trayIcon != null) return;

            string iconPath = System.IO.Path.Combine(appDir, "src", "icon.ico");

            trayIcon = new NotifyIcon
            {
                Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application,
                Visible = true,
                Text = "OptiSqueeze is running..."
            };

            var trayMenu = new System.Windows.Forms.ContextMenu();
            trayMenu.MenuItems.Add("Show Application UI", (s, e) => RestoreFromTray());
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Cancel Tasks & Exit App", (s, e) => {
                if (activeFFmpegProcess != null)
                {
                    try { activeFFmpegProcess.Kill(); } catch {}
                }
                trayIcon.Dispose();
                System.Windows.Application.Current.Shutdown();
            });
            trayIcon.ContextMenu = trayMenu;

            trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();

            if (trayIcon != null)
            {
                trayIcon.Dispose();
                trayIcon = null;
            }
        }
        #endregion

        #region Console / Logger Helpers
        private void ToggleConsole(object sender, RoutedEventArgs e)
        {
            isConsoleExpanded = !isConsoleExpanded;
            if (isConsoleExpanded)
            {
                btnToggleConsole.Content = "Hide FFmpeg Process Logs";
                consoleBorder.Height = 150;
            }
            else
            {
                btnToggleConsole.Content = "Show FFmpeg Process Logs";
                consoleBorder.Height = 0;
            }
        }

        private void AppendLog(string text)
        {
            Dispatcher.Invoke(() =>
            {
                consoleLogs.AppendText(text);
                if (isConsoleExpanded)
                {
                    consoleLogs.ScrollToEnd();
                }
            });
        }
        #endregion

        #region History File Storage (Extensionless hidden JSON parser)
        private void AddToHistoryDb(HistoryItem item)
        {
            var list = LoadHistoryList();
            list.Insert(0, item);
            WriteHistoryList(list);
        }

        private List<HistoryItem> LoadHistoryList()
        {
            var list = new List<HistoryItem>();
            if (!File.Exists(historyPath)) return list;

            try
            {
                string raw = File.ReadAllText(historyPath, Encoding.UTF8);
                // Manual parse extensionless JSON blocks
                var matches = Regex.Matches(raw, "\\{[^{}]+\\}");
                foreach (Match m in matches)
                {
                    string block = m.Value;
                    var parsed = ParseManualJsonBlock(block);
                    if (parsed != null) list.Add(parsed);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Manual JSON read failed: " + ex.Message);
            }
            return list;
        }

        private Dictionary<string, HistoryItem> LoadHistoryMap()
        {
            var map = new Dictionary<string, HistoryItem>();
            foreach (var item in LoadHistoryList())
            {
                if (item.Status == "Success" && !map.ContainsKey(item.FilePath))
                {
                    map.Add(item.FilePath, item);
                }
            }
            return map;
        }

        private void WriteHistoryList(List<HistoryItem> list)
        {
            var sb = new StringBuilder();
            sb.Append("[\n");
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                sb.Append("  {\n");
                sb.Append("    \"filePath\": \"" + EscapeJson(item.FilePath) + "\",\n");
                sb.Append("    \"outputPath\": \"" + EscapeJson(item.OutputPath ?? "") + "\",\n");
                sb.Append("    \"date\": \"" + EscapeJson(item.Date) + "\",\n");
                sb.Append("    \"status\": \"" + EscapeJson(item.Status) + "\",\n");
                sb.Append("    \"originalSize\": " + item.OriginalSize + ",\n");
                sb.Append("    \"compressedSize\": " + item.CompressedSize + ",\n");
                sb.Append("    \"savedBytes\": " + item.SavedBytes + ",\n");
                sb.Append("    \"duration\": " + item.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\n");
                sb.Append("    \"encoder\": \"" + EscapeJson(item.Encoder) + "\"\n");
                sb.Append("  }");
                if (i < list.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("]");

            try
            {
                // Remove hidden attributes temporarily to allow write
                if (File.Exists(historyPath))
                {
                    File.SetAttributes(historyPath, FileAttributes.Normal);
                }

                File.WriteAllText(historyPath, sb.ToString(), Encoding.UTF8);

                // Set attributes hidden
                File.SetAttributes(historyPath, FileAttributes.Hidden);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Manual JSON write failed: " + ex.Message);
            }
        }

        private async Task ClearHistoryDb()
        {
            if (System.Windows.MessageBox.Show("Are you sure you want to clear your dashboard stats and log history?", "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    if (File.Exists(historyPath))
                    {
                        File.SetAttributes(historyPath, FileAttributes.Normal);
                        File.Delete(historyPath);
                    }
                }
                catch {}
                
                LoadHistoryAndStats();
                await DetectHardwareEncoders();
            }
        }

        private void LoadHistoryAndStats()
        {
            var list = LoadHistoryList();
            
            long totalSaved = 0;
            int fileCount = 0;
            double avgRatioSum = 0;

            var successItems = new List<HistoryItem>();

            foreach (var item in list)
            {
                if (item.Status == "Success")
                {
                    successItems.Add(item);
                    totalSaved += item.SavedBytes;
                    fileCount++;
                    if (item.OriginalSize > 0)
                    {
                        avgRatioSum += (double)item.SavedBytes / item.OriginalSize;
                    }
                }
            }

            statsTotalFiles.Text = fileCount.ToString();
            statsTotalSaved.Text = FormatBytes(totalSaved);
            
            int ratioVal = 0;
            if (fileCount > 0) ratioVal = (int)Math.Round((avgRatioSum / fileCount) * 100);
            statsAvgRatio.Text = ratioVal + "%";

            historyListView.ItemsSource = null;
            historyListView.ItemsSource = list;
        }

        private string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private HistoryItem ParseManualJsonBlock(string jsonBlock)
        {
            try
            {
                var item = new HistoryItem();
                item.FilePath = ExtractJsonString(jsonBlock, "filePath");
                item.OutputPath = ExtractJsonString(jsonBlock, "outputPath");
                item.Date = ExtractJsonString(jsonBlock, "date");
                item.Status = ExtractJsonString(jsonBlock, "status");
                item.OriginalSize = ExtractJsonLong(jsonBlock, "originalSize");
                item.CompressedSize = ExtractJsonLong(jsonBlock, "compressedSize");
                item.SavedBytes = ExtractJsonLong(jsonBlock, "savedBytes");
                item.Duration = ExtractJsonDouble(jsonBlock, "duration");
                item.Encoder = ExtractJsonString(jsonBlock, "encoder");
                return item;
            }
            catch
            {
                return null;
            }
        }

        private string ExtractJsonString(string json, string key)
        {
            var match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : "";
        }

        private long ExtractJsonLong(string json, string key)
        {
            var match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(\\d+)");
            return match.Success ? long.Parse(match.Groups[1].Value) : 0;
        }

        private double ExtractJsonDouble(string json, string key)
        {
            var match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*([\\d\\.]+)");
            if (match.Success)
            {
                double val;
                if (double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val))
                    return val;
            }
            return 0;
        }
        #endregion

        #region Value Converters & String Format Helpers
        public static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 Bytes";
            string[] sizes = { "Bytes", "KB", "MB", "GB", "TB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return string.Format("{0:0.##} {1}", len, sizes[order]);
        }

        public static string FormatDurationString(double seconds)
        {
            if (seconds <= 0) return "Unknown";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.Hours > 0)
                return t.Hours + "h " + t.Minutes + "m " + t.Seconds + "s";
            return t.Minutes + "m " + t.Seconds + "s";
        }

        private SolidColorBrush GetBrush(string hex)
        {
            if (hex.StartsWith("rgba"))
            {
                // Parse rgba(r,g,b,a)
                var match = Regex.Match(hex, @"rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d\.]+)\s*\)");
                if (match.Success)
                {
                    byte r = byte.Parse(match.Groups[1].Value);
                    byte g = byte.Parse(match.Groups[2].Value);
                    byte b = byte.Parse(match.Groups[3].Value);
                    float a = float.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return new SolidColorBrush(Color.FromRgb(r, g, b)) { Opacity = a };
                }
            }
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        }
        #endregion
    }

    #region WPF Custom Value Converters
    public class BytesConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is long) return MainWindow.FormatBytes((long)value);
            return value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DurationConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double) return MainWindow.FormatDurationString((double)value);
            return value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PathFilenameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string) return System.IO.Path.GetFileName((string)value);
            return value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class SpaceReclaimedConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is long)
            {
                long val = (long)value;
                if (val > 0) return MainWindow.FormatBytes(val);
            }
            return "-";
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion
}
