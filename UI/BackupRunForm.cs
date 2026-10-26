// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Drawing;
using System.Windows.Forms;
using CopyexBackupAgent.Models;
using CopyexBackupAgent.Services;

namespace CopyexBackupAgent.UI
{
    /// <summary>
    /// Ventana compacta que aparece cuando se ejecuta la copia programada.
    /// Muestra el progreso, permite cancelarla y, al terminar, gestiona el apagado.
    /// </summary>
    internal class BackupRunForm : Form
    {
        private readonly BackupConfig _config;
        private readonly BackupRunner _runner;
        private readonly Label _lblState;
        private readonly Label _lblStats;
        private readonly Label _lblFile;
        private readonly RedProgressBar _bar;
        private readonly FlatButton _btnAction;
        private readonly Timer _autoClose;
        private bool _done;
        private bool _closeWhenDone;
        private int _autoCloseSeconds;

        public BackupRunForm(BackupConfig config)
        {
            _config = config;
            _runner = new BackupRunner();
            _runner.Progress += Runner_Progress;
            _runner.Completed += Runner_Completed;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            SuspendLayout();

            Text = "Copyex Backup Agent";
            Icon = Theme.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(500, 236);
            BackColor = Color.White;
            Font = Theme.Regular;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 56;
            header.BackColor = Color.White;

            PictureBox logo = new PictureBox();
            logo.Image = Theme.Logo;
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Location = new Point(20, 12);
            logo.Size = new Size(104, 33);
            header.Controls.Add(logo);

            Label caption = Theme.MakeLabel("Copia de seguridad programada", Theme.Bold, Theme.Muted, 140, 19);
            header.Controls.Add(caption);

            Panel line = new Panel();
            line.Dock = DockStyle.Top;
            line.Height = 3;
            line.BackColor = Theme.Brand;

            _lblState = Theme.MakeLabel("Preparando la copia...", Theme.Big, Theme.Text, 20, 76);

            _bar = new RedProgressBar();
            _bar.Location = new Point(22, 112);
            _bar.Size = new Size(456, 10);
            _bar.Marquee = true;

            _lblStats = new Label();
            _lblStats.Location = new Point(20, 130);
            _lblStats.Size = new Size(460, 20);
            _lblStats.ForeColor = Theme.Text;

            _lblFile = new Label();
            _lblFile.Location = new Point(20, 152);
            _lblFile.Size = new Size(460, 20);
            _lblFile.ForeColor = Theme.Muted;
            _lblFile.AutoEllipsis = true;
            _lblFile.UseMnemonic = false;

            _btnAction = new FlatButton();
            _btnAction.Text = "Cancelar copia";
            _btnAction.Location = new Point(338, 184);
            _btnAction.Size = new Size(140, 36);
            _btnAction.Click += BtnAction_Click;

            Controls.Add(_lblState);
            Controls.Add(_bar);
            Controls.Add(_lblStats);
            Controls.Add(_lblFile);
            Controls.Add(_btnAction);
            Controls.Add(line);
            Controls.Add(header);

            _autoClose = new Timer();
            _autoClose.Interval = 1000;
            _autoClose.Tick += AutoClose_Tick;

            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _runner.Start(_config, true);
        }

        private void Runner_Progress(BackupProgress p)
        {
            switch (p.Phase)
            {
                case BackupPhase.Preparing:
                    _lblState.Text = "Conectando con el destino...";
                    _bar.Marquee = true;
                    break;
                case BackupPhase.Scanning:
                    _lblState.Text = "Analizando archivos...";
                    _bar.Marquee = true;
                    _lblStats.Text = p.FilesTotal.ToString("N0") + " archivos encontrados";
                    break;
                case BackupPhase.Copying:
                    _bar.Marquee = false;
                    _bar.Value = p.Fraction;
                    _lblState.Text = "Copiando... " + (int)(p.Fraction * 100) + " %";
                    _lblStats.Text = MainForm.FormatProgress(p);
                    break;
                case BackupPhase.Finishing:
                    _bar.Marquee = true;
                    _lblState.Text = "Finalizando...";
                    break;
            }
            _lblFile.Text = p.CurrentItem;
        }

        private void Runner_Completed(BackupResult result)
        {
            _done = true;
            _bar.Marquee = false;
            _bar.Value = result.Status == BackupStatus.Ok || result.Status == BackupStatus.CompletedWithErrors ? 1 : 0;
            _lblFile.Text = string.Empty;
            _lblStats.Text = result.Message;
            _btnAction.Text = "Cerrar";
            _btnAction.Kind = ButtonKind.Primary;
            _btnAction.Invalidate();

            switch (result.Status)
            {
                case BackupStatus.Ok:
                    _lblState.Text = "Copia terminada correctamente";
                    _lblState.ForeColor = Theme.Success;
                    break;
                case BackupStatus.CompletedWithErrors:
                    _lblState.Text = "Copia terminada con incidencias";
                    _lblState.ForeColor = Theme.Warning;
                    break;
                case BackupStatus.Cancelled:
                    _lblState.Text = "Copia cancelada";
                    _lblState.ForeColor = Theme.Muted;
                    break;
                case BackupStatus.Skipped:
                    _lblState.Text = "Ya hay una copia en curso";
                    _lblState.ForeColor = Theme.Muted;
                    break;
                default:
                    _lblState.Text = "La copia no se ha podido hacer";
                    _lblState.ForeColor = Theme.Danger;
                    break;
            }

            if (_closeWhenDone)
            {
                Close();
                return;
            }

            if (SystemPower.ShouldShutdown(_config, result))
            {
                using (ShutdownForm f = new ShutdownForm(result.Message))
                    f.ShowDialog(this);
                Close();
                return;
            }

            // Si todo fue bien la ventana se cierra sola; si hubo problemas se queda a la vista.
            if (result.Status == BackupStatus.Ok || result.Status == BackupStatus.Skipped || result.Status == BackupStatus.Cancelled)
            {
                _autoCloseSeconds = 60;
                _autoClose.Start();
            }
        }

        private void AutoClose_Tick(object sender, EventArgs e)
        {
            _autoCloseSeconds--;
            _btnAction.Text = "Cerrar (" + _autoCloseSeconds + ")";
            _btnAction.Invalidate();
            if (_autoCloseSeconds <= 0)
            {
                _autoClose.Stop();
                Close();
            }
        }

        private void BtnAction_Click(object sender, EventArgs e)
        {
            if (_done)
            {
                Close();
                return;
            }

            if (MessageBox.Show(this, "¿Cancelar la copia de seguridad en curso?", "Copyex Backup Agent",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _btnAction.Enabled = false;
                _lblState.Text = "Cancelando...";
                _runner.Cancel();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_done && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (MessageBox.Show(this, "¿Cancelar la copia de seguridad en curso?", "Copyex Backup Agent",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _closeWhenDone = true;
                    _btnAction.Enabled = false;
                    _lblState.Text = "Cancelando...";
                    _runner.Cancel();
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _autoClose.Dispose();
                _runner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
