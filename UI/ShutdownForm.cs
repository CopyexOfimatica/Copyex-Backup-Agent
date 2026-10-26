// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Drawing;
using System.Windows.Forms;
using CopyexBackupAgent.Services;

namespace CopyexBackupAgent.UI
{
    /// <summary>
    /// Aviso de apagado con cuenta atrás. El apagado se programa en Windows al abrir la ventana,
    /// así que el equipo se apaga aunque nadie la toque (o aunque el programa se cierre).
    /// </summary>
    internal class ShutdownForm : Form
    {
        private const int Seconds = 60;
        private readonly Timer _timer;
        private readonly Label _lblCountdown;
        private readonly RedProgressBar _bar;
        private int _remaining = Seconds;
        private bool _scheduled;
        private bool _finished;

        public ShutdownForm(string summary)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            SuspendLayout();

            Text = "Apagado del equipo";
            Icon = Theme.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 250);
            BackColor = Color.White;
            Font = Theme.Regular;

            Panel band = new Panel();
            band.Dock = DockStyle.Top;
            band.Height = 6;
            band.BackColor = Theme.Brand;

            Label title = Theme.MakeLabel("El equipo se apagará", Theme.Value, Theme.Text, 28, 28);
            _lblCountdown = Theme.MakeLabel("en " + Seconds + " segundos", Theme.Big, Theme.Primary, 30, 62);

            Label info = new Label();
            info.Location = new Point(30, 98);
            info.Size = new Size(420, 40);
            info.ForeColor = Theme.Muted;
            info.Text = summary;
            info.AutoEllipsis = true;

            _bar = new RedProgressBar();
            _bar.Location = new Point(30, 146);
            _bar.Size = new Size(420, 8);
            _bar.Value = 1;

            FlatButton btnCancel = new FlatButton();
            btnCancel.Text = "Cancelar apagado";
            btnCancel.Kind = ButtonKind.Secondary;
            btnCancel.Location = new Point(150, 186);
            btnCancel.Size = new Size(150, 38);
            btnCancel.Click += delegate { CancelShutdown(); };

            FlatButton btnNow = new FlatButton();
            btnNow.Text = "Apagar ahora";
            btnNow.Kind = ButtonKind.Primary;
            btnNow.Location = new Point(310, 186);
            btnNow.Size = new Size(140, 38);
            btnNow.Click += delegate { _finished = true; SystemPower.ShutdownNow(); Close(); };

            Controls.Add(band);
            Controls.Add(title);
            Controls.Add(_lblCountdown);
            Controls.Add(info);
            Controls.Add(_bar);
            Controls.Add(btnCancel);
            Controls.Add(btnNow);
            CancelButton = btnCancel;

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += Timer_Tick;

            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _scheduled = SystemPower.ScheduleShutdown(Seconds);
            _timer.Start();
            Activate();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _remaining--;
            _bar.Value = Math.Max(0, _remaining) / (double)Seconds;
            _lblCountdown.Text = _remaining > 0 ? "en " + _remaining + " segundos" : "ahora";

            if (_remaining <= 0)
            {
                _timer.Stop();
                _finished = true;
                // Si Windows no aceptó el apagado programado, se fuerza ahora.
                if (!_scheduled)
                    SystemPower.ShutdownNow();
                Close();
            }
        }

        private void CancelShutdown()
        {
            _timer.Stop();
            if (!_finished)
                SystemPower.AbortShutdown();
            _finished = true;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
