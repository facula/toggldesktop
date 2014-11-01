﻿using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace TogglDesktop
{
    public partial class MainWindowController : TogglForm
    {
        [DllImport("user32.dll")]
        private static extern int ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hwnd, int msg, int wparam, int lparam);

        private const int wmNcLButtonDown = 0xA1;
        private const int wmNcLButtonUp = 0xA2;
        private const int HtBottomRight = 17;
        private bool isResizing = false;

        private LoginViewController loginViewController;
        private TimeEntryListViewController timeEntryListViewController;
        private TimeEntryEditViewController timeEntryEditViewController;
        private AboutWindowController aboutWindowController;
        private PreferencesWindowController preferencesWindowController;
        private FeedbackWindowController feedbackWindowController;
        private IdleNotificationWindowController idleNotificationWindowController;
        private EditForm editForm;
        private Control editableEntry;

        private bool isTracking = false;
        private bool isNetworkError = false;
        private Point defaultContentPosition =  new System.Drawing.Point(0, 0);
        private Point errorContentPosition = new System.Drawing.Point(0, 28);
        private bool remainOnTop = false;
        private bool topDisabled = false;
        private SizeF currentFactor;

        private static MainWindowController instance;

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public static readonly int SizeOf =
                Marshal.SizeOf(typeof(LASTINPUTINFO));

            [MarshalAs(UnmanagedType.U4)]
            public int cbSize;

            [MarshalAs(UnmanagedType.U4)]
            public int dwTime;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(out LASTINPUTINFO plii);

        [DllImport("user32", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowScrollBar(IntPtr hwnd, int wBar, [MarshalAs(UnmanagedType.Bool)] bool bShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd,
            int hWndInsertAfter, int x, int u, int cx, int cy, int uFlags);

        private const int HWND_TOPMOST = -1;
        private const int HWND_NOTOPMOST = -2;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOSIZE = 0x0001;
        private const int SB_HORZ = 0;

        public MainWindowController()
        {
            InitializeComponent();

            instance = this;
        }

        public void toggleMenu()
        {
            Point pt = new Point(Width - 90, 0);
            pt = PointToScreen(pt);
            trayIconMenu.Show(pt);
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            if (currentFactor != factor)
            {
                currentFactor = factor;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            hideHorizontalScrollBar();
            base.OnShown(e);
        }

        public static void DisableTop()
        {
            instance.topDisabled = true;
            instance.setWindowPos();
        }

        public static void EnableTop()
        {
            instance.topDisabled = false;
            instance.setWindowPos();
        }

        public void RemoveTrayIcon()
        {
            trayIcon.Visible = false;
        }

        private void MainWindowController_Load(object sender, EventArgs e)
        {
            troubleBox.BackColor = Color.FromArgb(239, 226, 121);
            contentPanel.Location = defaultContentPosition;

            Toggl.OnApp += OnApp;
            Toggl.OnError += OnError;
            Toggl.OnLogin += OnLogin;
            Toggl.OnTimeEntryList += OnTimeEntryList;
            Toggl.OnTimeEntryEditor += OnTimeEntryEditor;
            Toggl.OnOnlineState += OnOnlineState;
            Toggl.OnReminder += OnReminder;
            Toggl.OnURL += OnURL;
            Toggl.OnRunningTimerState += OnRunningTimerState;
            Toggl.OnStoppedTimerState += OnStoppedTimerState;
            Toggl.OnSettings += OnSettings;
            Toggl.OnIdleNotification += OnIdleNotification;

            loginViewController = new LoginViewController();
            timeEntryListViewController = new TimeEntryListViewController();
            timeEntryEditViewController = new TimeEntryEditViewController();
            aboutWindowController = new AboutWindowController();
            preferencesWindowController = new PreferencesWindowController();
            feedbackWindowController = new FeedbackWindowController();
            idleNotificationWindowController = new IdleNotificationWindowController();
            initEditForm();
            Utils.LoadWindowLocation(this, this.editForm);

            timeEntryListViewController.getListing().Scroll += MainWindowControllerEntries_Scroll;
            timeEntryListViewController.getListing().MouseWheel += MainWindowControllerEntries_Scroll;

            if (!Toggl.Start(TogglDesktop.Program.Version()))
            {
                try
                {
                    DisableTop();
                    MessageBox.Show("Missing callback. See the log file for details");
                } finally {
                    EnableTop();
                }
                TogglDesktop.Program.Shutdown(1);
            }

            aboutWindowController.initAndCheck();
        }

        private void MainWindowControllerEntries_Scroll(object sender, EventArgs e)
        {
            recalculatePopupPosition();
        }

        void OnRunningTimerState(Toggl.TimeEntry te)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnRunningTimerState(te); });
                return;
            }
            isTracking = true;
            enableMenuItems();
            displayTrayIcon(true);

            string newText = "Toggl Desktop";
            if (te.Description.Length > 0) {
                runningToolStripMenuItem.Text = te.Description.Replace("&", "&&");
                newText = te.Description + " - Toggl Desktop";
            }
            else
            {
                runningToolStripMenuItem.Text = "Timer is tracking";
            }
            if (newText.Length > 63)
            {
                newText = newText.Substring(0, 60) + "...";
            }
            Text = newText;
            if (trayIcon != null)
            {
                trayIcon.Text = Text;
            }
        }

        void OnStoppedTimerState()
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnStoppedTimerState(); });
                return;
            }
            isTracking = false;
            enableMenuItems();
            displayTrayIcon(true);

            runningToolStripMenuItem.Text = "Timer is not tracking";
            Text = "Toggl Desktop";
            trayIcon.Text = Text;
        }

        void OnSettings(bool open, Toggl.Settings settings)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnSettings(open, settings); });
                return;
            }
            remainOnTop = settings.OnTop;
            setWindowPos();
            timerIdleDetection.Enabled = settings.UseIdleDetection;
        }

        private void displayTrayIcon(bool is_online)
        {
            if (null == trayIcon)
            {
                return;
            }
            if (is_online)
            {
                if (TogglDesktop.Program.IsLoggedIn && isTracking)
                {
                    trayIcon.Icon = Properties.Resources.toggltray;
                    Icon = Properties.Resources.toggl;
                }
                else
                {
                    trayIcon.Icon = Properties.Resources.toggltray_inactive;
                    Icon = Properties.Resources.toggl_inactive;
                }
            }
            else
            {
                if (TogglDesktop.Program.IsLoggedIn && isTracking)
                {
                    trayIcon.Icon = Properties.Resources.toggl_offline_active;
                    Icon = Properties.Resources.toggl;
                }
                else
                {
                    trayIcon.Icon = Properties.Resources.toggl_offline_inactive;
                    Icon = Properties.Resources.toggl_inactive;
                }
            }
        }

        void OnOnlineState(bool is_online, string reason)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnOnlineState(is_online, reason); });
                return;
            }
            if (!is_online)
            {
                isNetworkError = true;

                errorLabel.Text = reason;
                troubleBox.Visible = true;
                contentPanel.Location = errorContentPosition;
                displayTrayIcon(false);
            }
            else if (isNetworkError)
            {
                isNetworkError = false;

                troubleBox.Visible = false;
                contentPanel.Location = defaultContentPosition;
                displayTrayIcon(true);
            }
        }

        void OnURL(string url)
        {
            Process.Start(url);
        }

        void OnApp(bool open)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnApp(open); });
                return;
            }
            if (open) {
                show();
            }
        }

        void OnError(string errmsg, bool user_error)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnError(errmsg, user_error); });
                return;
            }

            isNetworkError = false;

            errorLabel.Text = errmsg;
            troubleBox.Visible = true;
            contentPanel.Location = errorContentPosition;
        }

        void OnIdleNotification(
            string guid, string since, string duration, UInt64 started)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnIdleNotification(guid, since, duration, started); });
                return;
            }

            idleNotificationWindowController.ShowWindow(currentFactor);
        }

        void OnLogin(bool open, UInt64 user_id)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnLogin(open, user_id); });
                return;
            }
            if (open) {
                contentPanel.Controls.Remove(timeEntryListViewController);
                contentPanel.Controls.Remove(timeEntryEditViewController);
                contentPanel.Controls.Add(loginViewController);
                this.MinimumSize = new Size(loginViewController.MinimumSize.Width, loginViewController.MinimumSize.Height + 40);
                loginViewController.SetAcceptButton(this);
                resizeHandle.Visible = false;
            }
            enableMenuItems();
            displayTrayIcon(true);

            if (open || 0 == user_id)
            {
                runningToolStripMenuItem.Text = "Timer is not tracking";
            }
        }

        private void enableMenuItems()
        {
            bool isLoggedIn = TogglDesktop.Program.IsLoggedIn;

            newToolStripMenuItem.Enabled = isLoggedIn;
            continueToolStripMenuItem.Enabled = isLoggedIn && !isTracking;
            stopToolStripMenuItem.Enabled = isLoggedIn && isTracking;
            syncToolStripMenuItem.Enabled = isLoggedIn;
            logoutToolStripMenuItem.Enabled = isLoggedIn;
            clearCacheToolStripMenuItem.Enabled = isLoggedIn;
            sendFeedbackToolStripMenuItem.Enabled = isLoggedIn;
            openInBrowserToolStripMenuItem.Enabled = isLoggedIn;
        }

        void OnTimeEntryList(bool open, List<Toggl.TimeEntry> list)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnTimeEntryList(open, list); });
                return;
            }
            if (open)
            {
                troubleBox.Visible = false;
                contentPanel.Location = defaultContentPosition;
                contentPanel.Controls.Remove(loginViewController);
                this.MinimumSize = new Size(230, 190);
                contentPanel.Controls.Add(timeEntryListViewController);
                timeEntryListViewController.SetAcceptButton(this);
                resizeHandle.Visible = true;
                if (editForm.Visible)
                {
                    editForm.Hide();
                    editForm.GUID = null;
                }
            }
        }

        public static Control FindControlAtPoint(Control container, Point pos)
        {
            Control child;
            if (container.GetType() == typeof(TimeEntryCell) || container.GetType() == typeof(TimerEditViewController))
            {
                return container;
            }
            foreach (Control c in container.Controls)
            {
                if (c.Visible && c.Bounds.Contains(pos))
                {
                    child = FindControlAtPoint(c, new Point(pos.X - c.Left, pos.Y - c.Top));
                    if (child != null && (child.GetType() == typeof(TimeEntryCell) || child.GetType() == typeof(TimerEditViewController)))
                    {
                        return child;
                    }
                }
            }
            return null;
        }

        public static Control FindControlAtCursor(Form form)
        {
            Point pos = Cursor.Position;
            if (form.Bounds.Contains(pos))
                return FindControlAtPoint(form, form.PointToClient(Cursor.Position));
            return null;
        }

        private void initEditForm()
        {
            editForm = new EditForm
            {
                ControlBox = false,
                StartPosition = FormStartPosition.Manual
            };
            editForm.Controls.Add(timeEntryEditViewController);
            editForm.editView = timeEntryEditViewController;
        }

        public void PopupInput(Toggl.TimeEntry te)
        {
            if (te.GUID == editForm.GUID) {
                if (editableEntry.GetType() == typeof(TimeEntryCell))
                {
                    ((TimeEntryCell)editableEntry).opened = false;
                }
                editForm.CloseButton_Click(null, null);
                return;
            }
            if (editableEntry != null && editableEntry.GetType() == typeof(TimeEntryCell))
            {
                ((TimeEntryCell)editableEntry).opened = false;
            }
            editForm.reset();
            editableEntry = FindControlAtCursor(this);
            if (editableEntry == null) return;
            setEditFormLocation(te.DurationInSeconds < 0);
            editForm.GUID = te.GUID;
            editForm.Show();
        }

        void OnTimeEntryEditor(
            bool open,
            Toggl.TimeEntry te,
            string focused_field_name)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnTimeEntryEditor(open, te, focused_field_name); });
                return;
            }
            if (open)
            {
                contentPanel.Controls.Remove(loginViewController);
                this.MinimumSize = new Size(230, 190);
                timeEntryEditViewController.setupView(this, focused_field_name);
                PopupInput(te);                
            }
        }

        private void MainWindowController_FormClosing(object sender, FormClosingEventArgs e)
        {
            Utils.SaveWindowLocation(this, this.editForm);
            WinSparkle.win_sparkle_cleanup();

            if (!TogglDesktop.Program.ShuttingDown) {
                Hide();
                e.Cancel = true;
            }
            if (editForm.Visible)
            {
                if (editableEntry.GetType() == typeof(TimeEntryCell))
                {
                    ((TimeEntryCell)editableEntry).opened = false;
                }
                editForm.ClosePopup();
            }
        }

        private void buttonDismissError_Click(object sender, EventArgs e)
        {
            troubleBox.Visible = false;
            contentPanel.Location = defaultContentPosition;
        }

        private void sendFeedbackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            feedbackWindowController.Show();
            feedbackWindowController.TopMost = true;
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Visible)
            {
                Utils.SaveWindowLocation(this, this.editForm);
            }
            WinSparkle.win_sparkle_cleanup();

            TogglDesktop.Program.Shutdown(0);
        }

        private void toggleVisibility()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
                return;
            }
            if (Visible)
            {
                Hide();
                return;
            }
            show();
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Toggl.Start("", "", 0, 0);
        }

        private void continueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Toggl.ContinueLatest();
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
           Toggl.Stop();
        }

        private void showToolStripMenuItem_Click(object sender, EventArgs e)
        {
            show();
        }

        private void syncToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Toggl.Sync();
        }

        private void openInBrowserToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Toggl.OpenInBrowser();
        }

        private void preferencesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Toggl.EditPreferences();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            aboutWindowController.ShowUpdates();
        }

        private void logoutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Toggl.Logout();
        }

        private void show()
        {
            Show();
            TopMost = true;
            setWindowPos();
        }

        private void setWindowPos()
        {
            if (remainOnTop && !topDisabled)
            {
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                editForm.setWindowPos(HWND_TOPMOST);
            }
            else
            {
                SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                editForm.setWindowPos(HWND_NOTOPMOST);
            }
        }

        void OnReminder(string title, string informative_text)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate { OnReminder(title, informative_text); });
                return;
            }
            trayIcon.ShowBalloonTip(6000 * 100, title, informative_text, ToolTipIcon.None);
        }

        private void clearCacheToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult dr;
            try
            {
                DisableTop();
                dr = MessageBox.Show(
                    "This will remove your Toggl user data from this PC and log you out of the Toggl Desktop app. " +
                    "Any unsynced data will be lost." +
                    Environment.NewLine + "Do you want to continue?",
                    "Clear Cache",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            }
            finally
            {
                EnableTop();
            }
            if (DialogResult.Yes == dr) 
            {
                Toggl.ClearCache();
            }
        }

        private void trayIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            show();
        }

        private void timerIdleDetection_Tick(object sender, EventArgs e)
        {
            LASTINPUTINFO lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = Marshal.SizeOf(lastInputInfo);
            lastInputInfo.dwTime = 0;
            if (!GetLastInputInfo(out lastInputInfo)) {
                return;
            }
            int idle_seconds = unchecked(Environment.TickCount - (int)lastInputInfo.dwTime) / 1000;
            if (idle_seconds < 1) {
                return;
            }
            Toggl.SetIdleSeconds((ulong)idle_seconds);
        }

        private void trayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                toggleVisibility();
            }
        }

        private void MainWindowController_Activated(object sender, EventArgs e)
        {
            Toggl.SetWake();
        }

        private void MainWindowController_LocationChanged(object sender, EventArgs e)
        {
            recalculatePopupPosition();
        }

        private void setEditFormLocation(bool running)
        {
            if (Screen.AllScreens.Length > 1)
            {
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(DesktopBounds))
                    {
                        calculateEditFormPosition(running, s);
                        break;
                    }
                }
            }
            else
            {
                calculateEditFormPosition(running,Screen.PrimaryScreen);
            }
        }

        private void calculateEditFormPosition(bool running, Screen s)
        {
            Point ctrlpt = this.PointToScreen(editableEntry.Location);
            int arrowTop = 0;
            bool left = false;

            if (Location.X < editForm.Width && (s.Bounds.Width - (Location.X + Width)) < editForm.Width)
            {
                ctrlpt.X += (Width/2);
            }
            else
            {
                if ((editForm.Width + ctrlpt.X + this.Width) > s.Bounds.Width)
                {
                    ctrlpt.X -= editForm.Width;
                    left = true;
                }
                else
                {
                    ctrlpt.X += this.Width;
                }
            }

            if (running)
            {
                arrowTop = timeEntryListViewController.getEntriesTop() / 2;
            }
            else
            {
                ctrlpt.Y += timeEntryListViewController.getEntriesTop() + (((TimeEntryCell)editableEntry).getTopLocation()) - (editForm.Height / 2);
                if (editableEntry.GetType() == typeof(TimeEntryCell))
                {
                    ((TimeEntryCell)editableEntry).opened = true;
                }
            }
            editForm.setPlacement(left, arrowTop, ctrlpt, s);

        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (editForm.Visible)
                {
                    if (editableEntry.GetType() == typeof(TimeEntryCell))
                    {
                        ((TimeEntryCell)editableEntry).opened = false;
                    }
                    editForm.ClosePopup();
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void MainWindowController_SizeChanged(object sender, EventArgs e)
        {
            recalculatePopupPosition();
            if (this.timeEntryListViewController != null)
            {
                hideHorizontalScrollBar();
            }
            resizeHandle.Location = new Point(Width-16, Height-46);
        }

        private void recalculatePopupPosition()
        {
            if (editForm != null && editForm.Visible && editableEntry != null)
            {
                setEditFormLocation(editableEntry.GetType() == typeof(TimerEditViewController));
            }
        }

        private void hideHorizontalScrollBar()
        {
            ShowScrollBar(this.timeEntryListViewController.getListing().Handle, SB_HORZ, false);
        }

        private void resizeHandle_MouseDown(object sender, MouseEventArgs e)
        {
            isResizing = true;
        }

        private void resizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (isResizing)
            {
                isResizing = (e.Button == MouseButtons.Left);
                ReleaseCapture();
                int buttonEvent = (isResizing) ? wmNcLButtonDown : wmNcLButtonUp;
                SendMessage(Handle, buttonEvent, HtBottomRight, 0);
            }
        }
    }
}
