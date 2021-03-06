﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MangaRipper.Core.Models;
using MangaRipper.Helpers;
using MangaRipper.Presenters;
using NLog;
using MangaRipper.Core.Extensions;
using MangaRipper.Models;
using MangaRipper.Core.Plugins;
using MangaRipper.Core;
using System.Threading;

namespace MangaRipper.Forms
{
    public partial class FormMain : Form, IMainView
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private MainViewPresenter Presenter;
        private IEnumerable<IPlugin> pluginList;

        public FormMain(IEnumerable<IPlugin> pluginList, IWorkerController worker, ApplicationConfiguration applicationConfiguration)
        {
            InitializeComponent();
            this.pluginList = pluginList;
            Presenter = new MainViewPresenter(this, worker, applicationConfiguration);
        }

        public void SetChaptersProgress(string progress)
        {
            txtPercent.Text = progress;
        }

        public void SetChapterRows(IEnumerable<ChapterRow> chapters)
        {
            btnGetChapter.Enabled = true;
            dgvChapter.DataSource = chapters.ToList();
        }

        public void SetDownloadRows(IEnumerable<DownloadRow> chapters)
        {
            dgvQueueChapter.DataSource = chapters.ToList();
        }

        private async void BtnGetChapter_ClickAsync(object sender, EventArgs e)
        {
            btnGetChapter.Enabled = false;
            var titleUrl = cbTitleUrl.Text;
            try
            {
                await Presenter.GetChapterListAsync(titleUrl, checkBoxForPrefix.Checked);
            }
            catch (OperationCanceledException ex)
            {
                txtMessage.Text = @"Download cancelled! Reason: " + ex.Message;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                txtMessage.Text = @"Download cancelled! Reason: " + ex.Message;
                MessageBox.Show(ex.Message, ex.Source, MessageBoxButtons.OK, MessageBoxIcon.Information);
                btnGetChapter.Enabled = true;
                btnDownload.Enabled = true;
            }
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            var formats = GetOutputFormats().ToArray();
            if (formats.Length == 0)
            {
                MessageBox.Show("Please select at least one output format (Folder, Cbz...)");
                return;
            }
            var items = (from DataGridViewRow row in dgvChapter.Rows where row.Selected select row.DataBoundItem as ChapterRow).ToList();
            items.Reverse();
            Presenter.CreateDownloadRows(items, formats);
        }

        public string GetSavePath(ChapterRow chapter)
        {
            return Path.Combine(txtSaveTo.Text, chapter.DisplayName.RemoveFileNameInvalidChar());
        }

        private void BtnAddAll_Click(object sender, EventArgs e)
        {
            var formats = GetOutputFormats().ToArray();
            if (formats.Length == 0)
            {
                MessageBox.Show("Please select at least one output format (Folder, Cbz...)");
                return;
            }


            var items = (from DataGridViewRow row in dgvChapter.Rows select (ChapterRow)row.DataBoundItem).ToList();
            items.Reverse();
            Presenter.CreateDownloadRows(items, formats);
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            foreach (DataGridViewRow item in dgvQueueChapter.SelectedRows)
            {
                var chapter = (DownloadRow)item.DataBoundItem;

                if (chapter.IsBusy == false)
                    Presenter.Remove(chapter);
            }
        }

        private void BtnRemoveAll_Click(object sender, EventArgs e)
        {
            Presenter.RemoveAllChapterRows();
        }

        private async void BtnDownload_Click(object sender, EventArgs e)
        {
            try
            {
                btnDownload.Enabled = false;
                await Presenter.StartDownloadChaptersAsync();
            }
            catch (OperationCanceledException ex)
            {
                txtMessage.Text = @"Download cancelled! Reason: " + ex.Message;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                MessageBox.Show(ex.Message, ex.Source, MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtMessage.Text = @"Download cancelled! Reason: " + ex.Message;
            }
            finally
            {
                btnDownload.Enabled = true;
            }
        }

        private IEnumerable<OutputFormat> GetOutputFormats()
        {
            var outputFormats = new List<OutputFormat>();

            if (cbSaveFolder.Checked)
                outputFormats.Add(OutputFormat.Folder);

            if (cbSaveCbz.Checked)
                outputFormats.Add(OutputFormat.CBZ);

            return outputFormats;
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            Presenter.StopDownload();
        }

        private void BtnChangeSaveTo_Click(object sender, EventArgs e)
        {
            saveDestinationDirectoryBrowser.SelectedPath = txtSaveTo.Text;

            DialogResult dr = saveDestinationDirectoryBrowser.ShowDialog(this);
            if (dr == DialogResult.OK)
            {
                txtSaveTo.Text = saveDestinationDirectoryBrowser.SelectedPath;
            }

        }

        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(txtSaveTo.Text))
            {
                Process.Start(txtSaveTo.Text);
            }
            else
            {
                MessageBox.Show($"Directory \"{txtSaveTo.Text}\" doesn't exist.");
            }
        }

        private void DgvSupportedSites_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 1 && e.RowIndex >= 0)
                Process.Start(dgvSupportedSites.Rows[e.RowIndex].Cells[1].Value.ToString());
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            // Enables double-buffering to reduce flicker.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            var state = Presenter.LoadCommon();
            Size = state.WindowSize;
            Location = state.Location;
            WindowState = state.WindowState;
            txtSaveTo.Text = state.SaveTo;
            cbTitleUrl.Text = state.Url;
            cbSaveCbz.Checked = state.CbzChecked;

            dgvQueueChapter.AutoGenerateColumns = false;
            dgvChapter.AutoGenerateColumns = false;

            Text = $@"{Application.ProductName} {Application.ProductVersion}";

            try
            {
                foreach (var service in pluginList)
                {
                    var infor = service.GetInformation();
                    dgvSupportedSites.Rows.Add(infor.Name, infor.Link, infor.Language);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, ex.Message);
            }

            if (string.IsNullOrWhiteSpace(txtSaveTo.Text))
                txtSaveTo.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            Presenter.LoadDownloadChapterTasks();
            LoadBookmark();
            CheckForUpdate();
        }

        private async void CheckForUpdate()
        {
            if (Application.ProductVersion == "1.0.0.0")
                return;

            var latestVersion = await UpdateNotification.GetLatestVersion();
            if (UpdateNotification.GetLatestBuildNumber(latestVersion) >
                UpdateNotification.GetLatestBuildNumber(Application.ProductVersion))
            {
                Logger.Info($"Local version: {Application.ProductVersion}. Remote version: {latestVersion}");

                if (MessageBox.Show(
                    $"There's a new version: ({latestVersion}) - Click OK to open download page.",
                    Application.ProductName,
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information) == DialogResult.OK)
                {
                    Process.Start("https://github.com/NguyenDanPhuong/MangaRipper/releases");
                }
            }
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            var appConfig = Presenter.LoadCommon();
            switch (WindowState)
            {
                case FormWindowState.Normal:
                    appConfig.WindowSize = Size;
                    appConfig.Location = Location;
                    appConfig.WindowState = WindowState;
                    break;
                case FormWindowState.Maximized:
                    appConfig.WindowState = WindowState;
                    break;
                case FormWindowState.Minimized:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            appConfig.Url = cbTitleUrl.Text;
            appConfig.SaveTo = txtSaveTo.Text;
            appConfig.CbzChecked = cbSaveCbz.Checked;
            Presenter.SaveCommon(appConfig);
            Presenter.SaveDownloadChapterTasks();
        }

        private void FormMain_Paint(object sender, PaintEventArgs e)
        {
            // Method intentionally left empty.
        }

        private void LoadBookmark()
        {
            var bookmarks = Presenter.LoadBookMarks();
            cbTitleUrl.Items.Clear();
            if (bookmarks == null) return;
            foreach (var item in bookmarks)
                cbTitleUrl.Items.Add(item);
        }

        private void BtnAddBookmark_Click(object sender, EventArgs e)
        {
            var sc = Presenter.LoadBookMarks().ToList();
            if (sc.Contains(cbTitleUrl.Text) == false)
            {
                sc.Add(cbTitleUrl.Text);
                Presenter.SaveBookmarks(sc);
                LoadBookmark();
            }
        }

        private void BtnRemoveBookmark_Click(object sender, EventArgs e)
        {
            var sc = Presenter.LoadBookMarks().ToList();
            sc.Remove(cbTitleUrl.Text);
            Presenter.SaveBookmarks(sc);
            LoadBookmark();
        }

        private void TxtSaveTo_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Reject the user's keystroke if it's an invalid character for paths.
            if ((Keys)e.KeyChar != Keys.Back && Path.GetInvalidPathChars().Contains(e.KeyChar))
            {
                // Display a tooltip telling the user their input has been rejected.
                FormToolTip.Show($"The character \"{e.KeyChar}\" is a invalid for use in paths.", txtSaveTo);

                e.Handled = true;
            }
            else
            {
                FormToolTip.SetToolTip(txtSaveTo, string.Empty);
            }
        }

        private void CheckBoxForPrefix_CheckedChanged(object sender, EventArgs e)
        {
            Presenter.ChangePrefix(checkBoxForPrefix.Checked);
        }

        private void DataToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MangaRipper", "Data"));
        }

        private void LogsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MangaRipper", "Logs"));
        }

        private void WikiToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/NguyenDanPhuong/MangaRipper/wiki");
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var about = new AboutBox();
            about.ShowDialog(this);
        }

        private void BugReportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/NguyenDanPhuong/MangaRipper/wiki/Bug-Report");
        }

        private void ContributorsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/NguyenDanPhuong/MangaRipper/graphs/contributors");
        }
    }
}