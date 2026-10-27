// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using CopyexBackupAgent.Models;
using CopyexBackupAgent.Services;
using Timer = System.Windows.Forms.Timer;

namespace CopyexBackupAgent.UI
{
    public class MainForm : Form
    {
        internal static readonly int ShowMessageId = NativeMethods.RegisterWindowMessage("CopyexBackupAgent_Show");

        private const int PageWidth = 812;
        private const int MaxLogLines = 3000;
        private static readonly CultureInfo Es = new CultureInfo("es-ES");

        private readonly bool _startMinimized;
        private readonly BackupRunner _runner;
        private readonly Timer _monitor;
        private BackupConfig _config;
        private bool _loading;
        private bool _dirty;
        private bool _allowExit;
        private bool _exitAfterRun;
        private bool _trayHintShown;
        private bool _externalRunning;
        private int _logLines;
        private int _destRefreshId;

        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayRun;

        // estructura
        private Panel _sidebar;
        private Panel _contentHost;
        private Panel _footer;
        private Label _lblHeaderStatus;
        private Label _lblSidebarNext;
        private readonly List<NavButton> _nav = new List<NavButton>();
        private readonly List<Panel> _pages = new List<Panel>();

        // inicio
        private Label _lblLastValue, _lblLastSub, _lblNextValue, _lblNextSub, _lblStoredValue, _lblStoredSub;
        private Label _lblRunState, _lblRunStats, _lblRunSpeed, _lblRunFile, _lblDestValue;
        private RedProgressBar _progress;
        private FlatButton _btnRun;
        private CheckBox _chkShutdownQuick;

        // origen y destino
        private ListView _lvSources;
        private TextBox _txtDest, _txtUser, _txtPass, _txtExclusions;
        private FlatButton _btnTest;
        private NumericUpDown _numCopies;

        // programación
        private RadioButton _rbOff, _rbDaily, _rbWeekly;
        private readonly List<DayToggle> _days = new List<DayToggle>();
        private DateTimePicker _dtpTime;
        private CheckBox _chkShutdown, _chkOnlyOk, _chkStartup, _chkWake, _chkMissed;

        // historial y registro
        private ListView _lvBackups, _lvRuns;
        private Label _lblBackupsInfo;
        private RichTextBox _rtbLog;

        // pie
        private Label _lblDirty;
        private FlatButton _btnSave;

        public MainForm(bool startMinimized)
        {
            _startMinimized = startMinimized;
            _config = ConfigService.Load();

            _runner = new BackupRunner();
            _runner.Progress += Runner_Progress;
            _runner.Log += delegate(BackupLogEntry e) { AppendLog(e.Level, e.Message, e.Timestamp); };
            _runner.Completed += Runner_Completed;

            BuildUi();
            InitializeTray();

            _monitor = new Timer();
            _monitor.Interval = 5000;
            _monitor.Tick += Monitor_Tick;

            if (_startMinimized)
            {
                Opacity = 0;
                ShowInTaskbar = false;
            }
        }

        // =====================================================================================
        // Construcción de la interfaz
        // =====================================================================================

        private void BuildUi()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            SuspendLayout();

            Text = "Copyex Backup Agent";
            Icon = Theme.AppIcon;
            Font = Theme.Regular;
            BackColor = Theme.Background;
            ClientSize = new Size(1100, 800);
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;

            // --- cabecera con el logotipo bien visible
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 96;
            header.Width = 1100;
            header.BackColor = Color.White;

            PictureBox logo = new PictureBox();
            logo.Image = Theme.Logo;
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Location = new Point(28, 14);
            logo.Size = new Size(212, 68);
            logo.BackColor = Color.White;
            header.Controls.Add(logo);

            Panel sep = new Panel();
            sep.BackColor = Theme.Border;
            sep.Location = new Point(264, 26);
            sep.Size = new Size(1, 44);
            header.Controls.Add(sep);

            header.Controls.Add(Theme.MakeLabel("Backup Agent", Theme.Value, Theme.Text, 282, 24));
            header.Controls.Add(Theme.MakeLabel("Copias de seguridad automáticas en el NAS", Theme.Regular, Theme.Muted, 284, 54));

            _lblHeaderStatus = new Label();
            _lblHeaderStatus.AutoSize = false;
            _lblHeaderStatus.Size = new Size(210, 32);
            _lblHeaderStatus.Location = new Point(1100 - 28 - 210, 32);
            _lblHeaderStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblHeaderStatus.TextAlign = ContentAlignment.MiddleCenter;
            _lblHeaderStatus.Font = Theme.Bold;
            header.Controls.Add(_lblHeaderStatus);

            Panel redLine = new Panel();
            redLine.Dock = DockStyle.Top;
            redLine.Height = 4;
            redLine.BackColor = Theme.Brand;

            // --- menú lateral
            _sidebar = new Panel();
            _sidebar.Dock = DockStyle.Left;
            _sidebar.Width = 220;
            _sidebar.Height = 700;
            _sidebar.BackColor = Theme.Sidebar;

            AddNav("", "Inicio");
            AddNav("", "Origen y destino");
            AddNav("", "Programación");
            AddNav("", "Historial");
            AddNav("", "Registro");

            Label nextCaption = Theme.MakeLabel("PRÓXIMA COPIA", Theme.Small, Theme.SidebarMuted, 22, 700 - 78);
            nextCaption.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _lblSidebarNext = Theme.MakeLabel("—", Theme.Bold, Theme.SidebarText, 22, 700 - 56);
            _lblSidebarNext.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _sidebar.Controls.Add(nextCaption);
            _sidebar.Controls.Add(_lblSidebarNext);

            LinkLabel aboutLink = new LinkLabel();
            aboutLink.Text = "Acerca de · Licencia";
            aboutLink.AutoSize = true;
            aboutLink.Font = Theme.Small;
            aboutLink.LinkColor = Theme.SidebarMuted;
            aboutLink.ActiveLinkColor = Theme.SidebarText;
            aboutLink.VisitedLinkColor = Theme.SidebarMuted;
            aboutLink.LinkBehavior = LinkBehavior.HoverUnderline;
            aboutLink.BackColor = Color.Transparent;
            aboutLink.Location = new Point(22, 700 - 26);
            aboutLink.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            aboutLink.LinkClicked += delegate { AboutInfo.Show(this); };
            _sidebar.Controls.Add(aboutLink);

            // --- zona de contenido + pie con "Guardar"
            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.BackColor = Theme.Background;

            _contentHost = new Panel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.Padding = new Padding(32, 26, 24, 8);
            _contentHost.BackColor = Theme.Background;

            BuildFooter();

            right.Controls.Add(_contentHost);
            right.Controls.Add(_footer);

            BuildHomePage();
            BuildSourcesPage();
            BuildSchedulePage();
            BuildHistoryPage();
            BuildLogPage();
            foreach (Panel p in _pages) p.Dock = DockStyle.Fill;

            Controls.Add(right);
            Controls.Add(_sidebar);
            Controls.Add(redLine);
            Controls.Add(header);

            ResumeLayout(false);
            PerformLayout();
        }

        private void AddNav(string glyph, string text)
        {
            int index = _nav.Count;
            NavButton b = new NavButton(glyph, text);
            b.Location = new Point(0, 18 + index * 46);
            b.Size = new Size(220, 44);
            b.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            b.Click += delegate { ShowPage(index); };
            _nav.Add(b);
            _sidebar.Controls.Add(b);
        }

        private Panel CreatePage(string title, string subtitle)
        {
            Panel page = new Panel();
            // Se crea con su tamaño real antes de añadir controles y se acopla al final (ver BuildUi);
            // si no, los controles anclados a la derecha se calculan mal.
            page.Size = new Size(PageWidth + 12, 680);
            page.BackColor = Theme.Background;
            page.AutoScroll = true;
            page.Visible = false;
            page.Controls.Add(Theme.MakeLabel(title, Theme.Title, Theme.Text, 0, 0));
            page.Controls.Add(Theme.MakeLabel(subtitle, Theme.Regular, Theme.Muted, 2, 40));
            _contentHost.Controls.Add(page);
            _pages.Add(page);
            return page;
        }

        private Card AddCard(Panel page, string title, string subtitle, int y, int height)
        {
            Card c = new Card();
            c.Location = new Point(0, y);
            c.Size = new Size(PageWidth, height);
            c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            if (title != null)
                c.Controls.Add(Theme.MakeLabel(title, Theme.Section, Theme.Text, 22, 18));
            if (subtitle != null)
                c.Controls.Add(Theme.MakeLabel(subtitle, Theme.Regular, Theme.Muted, 22, 44));
            page.Controls.Add(c);
            return c;
        }

        private static FlatButton MakeButton(string text, ButtonKind kind, int x, int y, int width)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.Kind = kind;
            b.Location = new Point(x, y);
            b.Size = new Size(width, 36);
            return b;
        }

        private static CheckBox MakeCheck(string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Location = new Point(x, y);
            c.ForeColor = Theme.Text;
            c.Cursor = Cursors.Hand;
            return c;
        }

        private static Label MakeFillLabel(Font font, Color color, int x, int y, int width)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.AutoEllipsis = true;
            l.UseMnemonic = false;
            l.Font = font;
            l.ForeColor = color;
            l.Location = new Point(x, y);
            l.Size = new Size(width, font.Height + 4);
            l.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            return l;
        }

        private static ListView MakeListView(params object[] columns)
        {
            ListView lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.HideSelection = false;
            lv.BorderStyle = BorderStyle.None;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.Font = Theme.Regular;
            ImageList rowHeight = new ImageList();
            rowHeight.ImageSize = new Size(1, 26);
            lv.SmallImageList = rowHeight;
            for (int i = 0; i < columns.Length; i += 2)
                lv.Columns.Add((string)columns[i], (int)columns[i + 1]);
            lv.Resize += delegate { FillLastColumn(lv); };
            return lv;
        }

        private static void FillLastColumn(ListView lv)
        {
            if (lv.Columns.Count == 0) return;
            int used = 0;
            for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
            int w = lv.ClientSize.Width - used - 4;
            if (w > 60) lv.Columns[lv.Columns.Count - 1].Width = w;
        }

        private BorderPanel Bordered(Control inner, int x, int y, int width, int height, AnchorStyles anchor)
        {
            BorderPanel p = new BorderPanel(inner);
            p.Location = new Point(x, y);
            p.Size = new Size(width, height);
            p.Anchor = anchor;
            return p;
        }

        // ---------------------------------------------------------------- Inicio

        private void BuildHomePage()
        {
            Panel page = CreatePage("Inicio", "Estado de las copias de seguridad de este equipo.");

            TableLayoutPanel stats = new TableLayoutPanel();
            stats.Location = new Point(0, 78);
            stats.Size = new Size(PageWidth, 116);
            stats.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            stats.ColumnCount = 3;
            stats.RowCount = 1;
            stats.BackColor = Theme.Background;
            for (int i = 0; i < 3; i++)
                stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            stats.Controls.Add(MakeStatCard("ÚLTIMA COPIA", out _lblLastValue, out _lblLastSub, new Padding(0, 0, 8, 0)), 0, 0);
            stats.Controls.Add(MakeStatCard("PRÓXIMA COPIA", out _lblNextValue, out _lblNextSub, new Padding(4, 0, 4, 0)), 1, 0);
            stats.Controls.Add(MakeStatCard("COPIAS GUARDADAS", out _lblStoredValue, out _lblStoredSub, new Padding(8, 0, 0, 0)), 2, 0);
            page.Controls.Add(stats);

            Card run = AddCard(page, "Copia de seguridad", null, 210, 254);
            _lblRunState = Theme.MakeLabel("Lista para copiar", Theme.Big, Theme.Text, 22, 54);
            _progress = new RedProgressBar();
            _progress.Location = new Point(24, 94);
            _progress.Size = new Size(PageWidth - 48, 10);
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblRunStats = MakeFillLabel(Theme.Regular, Theme.Text, 22, 114, PageWidth - 44);
            _lblRunSpeed = MakeFillLabel(Theme.Regular, Theme.Muted, 22, 136, PageWidth - 44);
            _lblRunFile = MakeFillLabel(Theme.Small, Theme.Muted, 22, 158, PageWidth - 44);

            _btnRun = MakeButton("Hacer copia ahora", ButtonKind.Primary, 22, 192, 230);
            _btnRun.Height = 44;
            _btnRun.Font = Theme.Section;
            _btnRun.Click += BtnRun_Click;

            _chkShutdownQuick = MakeCheck("Apagar el equipo al terminar", 274, 204);
            _chkShutdownQuick.CheckedChanged += ChkShutdownQuick_CheckedChanged;

            run.Controls.Add(_lblRunState);
            run.Controls.Add(_progress);
            run.Controls.Add(_lblRunStats);
            run.Controls.Add(_lblRunSpeed);
            run.Controls.Add(_lblRunFile);
            run.Controls.Add(_btnRun);
            run.Controls.Add(_chkShutdownQuick);

            Card dest = AddCard(page, null, null, 480, 96);
            dest.Controls.Add(Theme.MakeLabel("DESTINO DE LAS COPIAS", Theme.Small, Theme.Muted, 22, 18));
            _lblDestValue = MakeFillLabel(Theme.Bold, Theme.Text, 22, 40, PageWidth - 230);
            dest.Controls.Add(_lblDestValue);
            dest.Controls.Add(Theme.MakeLabel("Cada copia se guarda en una carpeta con su fecha y hora.", Theme.Small, Theme.Muted, 22, 64));
            FlatButton open = MakeButton("Abrir destino", ButtonKind.Secondary, PageWidth - 22 - 160, 30, 160);
            open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            open.Click += delegate { OpenFolder(_config.Destination); };
            dest.Controls.Add(open);
        }

        private Card MakeStatCard(string caption, out Label value, out Label sub, Padding margin)
        {
            Card c = new Card();
            c.Dock = DockStyle.Fill;
            c.Margin = margin;
            c.Controls.Add(Theme.MakeLabel(caption, Theme.Small, Theme.Muted, 18, 16));
            value = MakeFillLabel(Theme.Value, Theme.Text, 16, 38, 230);
            value.Text = "—";
            sub = MakeFillLabel(Theme.Small, Theme.Muted, 18, 76, 230);
            c.Controls.Add(value);
            c.Controls.Add(sub);
            Label v = value, s = sub;
            c.Resize += delegate
            {
                v.Width = Math.Max(40, c.ClientSize.Width - 30);
                s.Width = Math.Max(40, c.ClientSize.Width - 30);
            };
            return c;
        }

        // ---------------------------------------------------------------- Origen y destino

        private void BuildSourcesPage()
        {
            Panel page = CreatePage("Origen y destino", "Qué se copia y dónde se guarda.");

            Card what = AddCard(page, "Qué copiar", "Carpetas y archivos incluidos en cada copia. También puedes arrastrarlos a la lista.", 78, 256);
            _lvSources = MakeListView("Ruta", 560, "Tipo", 110, "Estado", 120);
            _lvSources.AllowDrop = true;
            _lvSources.DragEnter += delegate(object s, DragEventArgs e)
            {
                e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            _lvSources.DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null)
                    foreach (string p in paths) AddSource(p);
            };
            _lvSources.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) RemoveSelectedSources(); };
            what.Controls.Add(Bordered(_lvSources, 22, 74, PageWidth - 44, 124, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right));

            FlatButton addFolder = MakeButton("Añadir carpeta", ButtonKind.Primary, 22, 206, 150);
            addFolder.Click += BtnAddFolder_Click;
            FlatButton addFiles = MakeButton("Añadir archivos", ButtonKind.Secondary, 180, 206, 150);
            addFiles.Click += BtnAddFiles_Click;
            FlatButton remove = MakeButton("Quitar", ButtonKind.Secondary, 338, 206, 110);
            remove.Click += delegate { RemoveSelectedSources(); };
            what.Controls.Add(addFolder);
            what.Controls.Add(addFiles);
            what.Controls.Add(remove);

            Card where = AddCard(page, "Dónde guardarlas", "Carpeta del NAS o disco de copias. Recomendado: ruta de red, por ejemplo \\\\NAS\\Copias\\Oficina", 350, 222);
            _txtDest = new TextBox();
            _txtDest.Location = new Point(22, 78);
            _txtDest.Size = new Size(PageWidth - 44 - 290, 26);
            _txtDest.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _txtDest.Font = Theme.Regular;
            where.Controls.Add(_txtDest);

            FlatButton browse = MakeButton("Examinar...", ButtonKind.Secondary, PageWidth - 22 - 278, 74, 130);
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Click += BtnBrowse_Click;
            _btnTest = MakeButton("Probar conexión", ButtonKind.Secondary, PageWidth - 22 - 140, 74, 140);
            _btnTest.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnTest.Click += BtnTest_Click;
            where.Controls.Add(browse);
            where.Controls.Add(_btnTest);

            where.Controls.Add(Theme.MakeLabel("Usuario del NAS (opcional)", Theme.Regular, Theme.Text, 22, 124));
            _txtUser = new TextBox();
            _txtUser.Location = new Point(22, 146);
            _txtUser.Size = new Size(250, 26);
            where.Controls.Add(_txtUser);

            where.Controls.Add(Theme.MakeLabel("Contraseña", Theme.Regular, Theme.Text, 292, 124));
            _txtPass = new TextBox();
            _txtPass.Location = new Point(292, 146);
            _txtPass.Size = new Size(250, 26);
            _txtPass.UseSystemPasswordChar = true;
            where.Controls.Add(_txtPass);

            where.Controls.Add(Theme.MakeLabel("Déjalos vacíos si Windows ya entra en el NAS sin pedir contraseña. La contraseña se guarda cifrada.",
                Theme.Small, Theme.Muted, 22, 184));

            Card keep = AddCard(page, "Histórico y exclusiones", null, 588, 190);
            keep.Controls.Add(Theme.MakeLabel("Copias a conservar", Theme.Regular, Theme.Text, 22, 56));
            _numCopies = new NumericUpDown();
            _numCopies.Location = new Point(22, 80);
            _numCopies.Size = new Size(90, 26);
            _numCopies.Minimum = 1;
            _numCopies.Maximum = 999;
            _numCopies.Font = Theme.Bold;
            _numCopies.TextAlign = HorizontalAlignment.Center;
            keep.Controls.Add(_numCopies);
            Label keepHint = MakeFillLabel(Theme.Regular, Theme.Muted, 128, 82, PageWidth - 150);
            keepHint.Text = "Al terminar cada copia se borran automáticamente las más antiguas que superen este número.";
            keep.Controls.Add(keepHint);

            keep.Controls.Add(Theme.MakeLabel("No copiar estos archivos (separados por ;)", Theme.Regular, Theme.Text, 22, 118));
            _txtExclusions = new TextBox();
            _txtExclusions.Location = new Point(22, 142);
            _txtExclusions.Size = new Size(PageWidth - 44, 26);
            _txtExclusions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            keep.Controls.Add(_txtExclusions);

            _txtDest.TextChanged += delegate { MarkDirty(); };
            _txtUser.TextChanged += delegate { MarkDirty(); };
            _txtPass.TextChanged += delegate { MarkDirty(); };
            _txtExclusions.TextChanged += delegate { MarkDirty(); };
            _numCopies.ValueChanged += delegate { MarkDirty(); };
        }

        // ---------------------------------------------------------------- Programación

        private void BuildSchedulePage()
        {
            Panel page = CreatePage("Programación", "Cuándo se hace la copia automática y qué hacer al terminar.");

            Card when = AddCard(page, "Cuándo", null, 78, 232);
            _rbOff = new RadioButton();
            _rbDaily = new RadioButton();
            _rbWeekly = new RadioButton();
            RadioButton[] rbs = { _rbOff, _rbDaily, _rbWeekly };
            string[] texts = { "Desactivada (solo copias manuales)", "Todos los días", "Solo algunos días de la semana" };
            for (int i = 0; i < rbs.Length; i++)
            {
                rbs[i].Text = texts[i];
                rbs[i].AutoSize = true;
                rbs[i].Location = new Point(22, 56 + i * 28);
                rbs[i].Cursor = Cursors.Hand;
                rbs[i].CheckedChanged += delegate { UpdateScheduleUi(); MarkDirty(); };
                when.Controls.Add(rbs[i]);
            }

            string[] letters = { "L", "M", "X", "J", "V", "S", "D" };
            DayOfWeek[] order = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                                  DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            for (int i = 0; i < 7; i++)
            {
                DayToggle t = new DayToggle(letters[i], order[i]);
                t.Location = new Point(44 + i * 48, 146);
                t.CheckedChanged += delegate { MarkDirty(); };
                _days.Add(t);
                when.Controls.Add(t);
            }

            when.Controls.Add(Theme.MakeLabel("Hora", Theme.Regular, Theme.Text, 440, 56));
            _dtpTime = new DateTimePicker();
            _dtpTime.Format = DateTimePickerFormat.Custom;
            _dtpTime.CustomFormat = "HH:mm";
            _dtpTime.ShowUpDown = true;
            _dtpTime.Font = Theme.Big;
            _dtpTime.Location = new Point(440, 80);
            _dtpTime.Size = new Size(110, 32);
            _dtpTime.ValueChanged += delegate { MarkDirty(); };
            when.Controls.Add(_dtpTime);

            when.Controls.Add(Theme.MakeLabel("La copia automática se hace con la sesión de Windows iniciada (puede estar bloqueada).",
                Theme.Small, Theme.Muted, 22, 196));

            Card end = AddCard(page, "Al terminar la copia", null, 326, 150);
            _chkShutdown = MakeCheck("Apagar el equipo", 22, 56);
            _chkShutdown.CheckedChanged += ChkShutdown_CheckedChanged;
            _chkOnlyOk = MakeCheck("Solo si la copia termina sin ningún error", 44, 84);
            _chkOnlyOk.CheckedChanged += delegate { MarkDirty(); };
            end.Controls.Add(_chkShutdown);
            end.Controls.Add(_chkOnlyOk);
            end.Controls.Add(Theme.MakeLabel("Antes de apagar aparece un aviso de 60 segundos con la opción de cancelarlo. Si nadie lo toca, el equipo se apaga.",
                Theme.Small, Theme.Muted, 22, 116));

            Card agent = AddCard(page, "Agente", null, 492, 152);
            _chkStartup = MakeCheck("Iniciar con Windows (se queda junto al reloj, sin molestar)", 22, 56);
            _chkWake = MakeCheck("Despertar el equipo de la suspensión para hacer la copia", 22, 84);
            _chkMissed = MakeCheck("Si el equipo estaba apagado a la hora programada, hacer la copia al encenderlo", 22, 112);
            foreach (CheckBox c in new[] { _chkStartup, _chkWake, _chkMissed })
            {
                c.CheckedChanged += delegate { MarkDirty(); };
                agent.Controls.Add(c);
            }
        }

        // ---------------------------------------------------------------- Historial

        private void BuildHistoryPage()
        {
            Panel page = CreatePage("Historial", "Copias guardadas en el destino y últimas ejecuciones.");

            Card stored = AddCard(page, "Copias guardadas en el destino", null, 78, 300);
            _lblBackupsInfo = Theme.MakeLabel(string.Empty, Theme.Small, Theme.Muted, 22, 44);
            stored.Controls.Add(_lblBackupsInfo);

            FlatButton del = MakeButton("Eliminar", ButtonKind.Secondary, PageWidth - 22 - 110, 16, 110);
            FlatButton open = MakeButton("Abrir", ButtonKind.Secondary, PageWidth - 22 - 228, 16, 110);
            FlatButton refresh = MakeButton("Actualizar", ButtonKind.Secondary, PageWidth - 22 - 346, 16, 110);
            foreach (FlatButton b in new[] { del, open, refresh })
            {
                b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                stored.Controls.Add(b);
            }
            del.Click += BtnDeleteBackup_Click;
            open.Click += delegate
            {
                BackupFolderInfo info = SelectedBackup();
                if (info != null) OpenFolder(info.Path);
            };
            refresh.Click += delegate { RefreshDestinationAsync(); };

            _lvBackups = MakeListView("Fecha", 170, "Equipo", 140, "Archivos", 100, "Tamaño", 100, "Incidencias", 90, "Carpeta", 180);
            _lvBackups.DoubleClick += delegate
            {
                BackupFolderInfo info = SelectedBackup();
                if (info != null) OpenFolder(info.Path);
            };
            stored.Controls.Add(Bordered(_lvBackups, 22, 66, PageWidth - 44, 214, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right));

            Card runs = AddCard(page, "Últimas ejecuciones", null, 394, 260);
            runs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lvRuns = MakeListView("Fecha", 150, "Tipo", 100, "Resultado", 150, "Archivos", 90, "Tamaño", 90, "Duración", 90, "Detalle", 200);
            runs.Controls.Add(Bordered(_lvRuns, 22, 56, PageWidth - 44, 184,
                AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
        }

        // ---------------------------------------------------------------- Registro

        private void BuildLogPage()
        {
            Panel page = CreatePage("Registro", "Actividad de esta sesión. Cada copia guarda además su resumen en el fichero " + BackupService.MarkerFileName + ".");

            FlatButton save = MakeButton("Guardar...", ButtonKind.Secondary, PageWidth - 120, 4, 120);
            save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            save.Click += BtnSaveLog_Click;
            FlatButton clear = MakeButton("Limpiar", ButtonKind.Secondary, PageWidth - 248, 4, 120);
            clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.Click += delegate { _rtbLog.Clear(); _logLines = 0; };
            page.Controls.Add(save);
            page.Controls.Add(clear);

            Card card = AddCard(page, null, null, 78, 560);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            card.Padding = new Padding(14, 10, 4, 10);
            _rtbLog = new RichTextBox();
            _rtbLog.Dock = DockStyle.Fill;
            _rtbLog.BorderStyle = BorderStyle.None;
            _rtbLog.ReadOnly = true;
            _rtbLog.BackColor = Color.White;
            _rtbLog.Font = Theme.Mono;
            _rtbLog.WordWrap = false;
            _rtbLog.HideSelection = false;
            card.Controls.Add(_rtbLog);
        }

        // ---------------------------------------------------------------- Pie

        private void BuildFooter()
        {
            _footer = new Panel();
            _footer.Dock = DockStyle.Bottom;
            _footer.Height = 66;
            _footer.Width = 880;
            _footer.BackColor = Color.White;
            _footer.Visible = false;

            Panel line = new Panel();
            line.Dock = DockStyle.Top;
            line.Height = 1;
            line.BackColor = Theme.Border;
            _footer.Controls.Add(line);

            _lblDirty = Theme.MakeLabel(string.Empty, Theme.Regular, Theme.Muted, 32, 24);
            _footer.Controls.Add(_lblDirty);

            _btnSave = MakeButton("Guardar cambios", ButtonKind.Primary, 880 - 24 - 180, 14, 180);
            _btnSave.Height = 40;
            _btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnSave.Click += delegate { SaveSettings(true); };
            _footer.Controls.Add(_btnSave);
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visible = i == index;
                _nav[i].Selected = i == index;
            }
            _footer.Visible = index == 1 || index == 2;
            if (index == 3)
            {
                RefreshRuns();
                RefreshDestinationAsync();
            }
        }

        // =====================================================================================
        // Ciclo de vida
        // =====================================================================================

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LoadConfigIntoUi();
            ShowPage(0);
            SetRunningUi(false);
            UpdateHeaderStatus();
            RefreshHome();
            RefreshRuns();
            AppendLog(BackupLogLevel.Info, "Copyex Backup Agent iniciado.");
            _monitor.Start();

            // Si el programa se ha reinstalado en otra carpeta, se actualizan las rutas de la tarea y del inicio.
            StartupService.RefreshPathIfEnabled();
            BackupConfig cfg = _config.Clone();
            if (cfg.Schedule != ScheduleMode.Disabled)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    SchedulerResult r = SchedulerService.Apply(cfg);
                    if (!r.Success)
                        SafeInvoke(delegate { AppendLog(BackupLogLevel.Warning, "Programación: " + r.Message); });
                });
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_startMinimized)
            {
                BeginInvoke(new Action(delegate
                {
                    HideToTray(false);
                    Opacity = 1;
                }));
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && _tray != null)
                BeginInvoke(new Action(delegate { HideToTray(true); }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_allowExit || e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                if (_tray != null) _tray.Visible = false;
                base.OnFormClosing(e);
                return;
            }

            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray(true);
            }
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ShowMessageId && ShowMessageId != 0)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        private void InitializeTray()
        {
            _trayMenu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("Abrir Copyex Backup Agent");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += delegate { ShowFromTray(); };
            _trayRun = new ToolStripMenuItem("Hacer copia ahora");
            _trayRun.Click += delegate { ShowFromTray(); ShowPage(0); BtnRun_Click(this, EventArgs.Empty); };
            ToolStripMenuItem about = new ToolStripMenuItem("Acerca de...");
            about.Click += delegate { AboutInfo.Show(Visible ? this : null); };
            ToolStripMenuItem exit = new ToolStripMenuItem("Salir");
            exit.Click += delegate { ExitApplication(); };
            _trayMenu.Items.Add(open);
            _trayMenu.Items.Add(_trayRun);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(about);
            _trayMenu.Items.Add(exit);

            _tray = new NotifyIcon();
            _tray.Icon = Icon ?? SystemIcons.Application;
            _tray.Text = "Copyex Backup Agent";
            _tray.ContextMenuStrip = _trayMenu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowFromTray(); };
            _tray.BalloonTipClicked += delegate { ShowFromTray(); };
        }

        private void HideToTray(bool showHint)
        {
            ShowInTaskbar = false;
            Hide();
            if (showHint && !_trayHintShown && _tray != null)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(4000, "Copyex Backup Agent", "Sigue funcionando junto al reloj. Haz doble clic en el icono para abrirlo.", ToolTipIcon.Info);
            }
        }

        private void ShowFromTray()
        {
            Opacity = 1;
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            RefreshHome();
        }

        private void ExitApplication()
        {
            if (_runner.IsBusy)
            {
                ShowFromTray();
                if (MessageBox.Show(this, "Hay una copia en curso. ¿Cancelarla y salir?", "Copyex Backup Agent",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                _exitAfterRun = true;
                _runner.Cancel();
                return;
            }

            _allowExit = true;
            if (_tray != null) _tray.Visible = false;
            Close();
        }

        private void Monitor_Tick(object sender, EventArgs e)
        {
            if (!_runner.IsBusy)
            {
                bool external = BackupRunner.IsBackupRunningElsewhere();
                if (external != _externalRunning)
                {
                    _externalRunning = external;
                    SetRunningUi(false);
                    if (!external)
                    {
                        RefreshHome();
                        RefreshRuns();
                    }
                }
            }
            UpdateNextRunLabels();
        }

        // =====================================================================================
        // Configuración
        // =====================================================================================

        private void LoadConfigIntoUi()
        {
            _loading = true;
            try
            {
                _lvSources.Items.Clear();
                foreach (string s in _config.Sources) AddSource(s);

                _txtDest.Text = _config.Destination ?? string.Empty;
                _txtUser.Text = _config.NasUser ?? string.Empty;
                _txtPass.Text = ConfigService.DecryptPassword(_config.NasPassword);
                _txtExclusions.Text = _config.Exclusions ?? string.Empty;
                _numCopies.Value = Math.Max(_numCopies.Minimum, Math.Min(_numCopies.Maximum, _config.CopiesToKeep));

                _rbOff.Checked = _config.Schedule == ScheduleMode.Disabled;
                _rbDaily.Checked = _config.Schedule == ScheduleMode.Daily;
                _rbWeekly.Checked = _config.Schedule == ScheduleMode.Weekly;
                foreach (DayToggle t in _days)
                    t.Checked = _config.WeeklyDays.Contains(t.Day);
                if (_config.WeeklyDays.Count == 0)
                    for (int i = 0; i < 5; i++) _days[i].Checked = true;

                DateTime time;
                _dtpTime.Value = SchedulerService.TryParseTime(_config.ScheduleTime, out time)
                    ? DateTime.Today.Add(time.TimeOfDay)
                    : DateTime.Today.AddHours(20);

                _chkShutdown.Checked = _config.ShutdownAfterBackup;
                _chkShutdownQuick.Checked = _config.ShutdownAfterBackup;
                _chkOnlyOk.Checked = _config.ShutdownOnlyIfNoErrors;
                _chkStartup.Checked = StartupService.IsEnabled();
                _chkWake.Checked = _config.WakeToRun;
                _chkMissed.Checked = _config.RunMissedBackup;
                UpdateScheduleUi();
            }
            finally
            {
                _loading = false;
            }
            SetDirty(false, null);
        }

        private BackupConfig ReadConfigFromUi()
        {
            BackupConfig c = _config.Clone();
            c.Sources = new List<string>();
            foreach (ListViewItem item in _lvSources.Items) c.Sources.Add(item.Text);

            c.Destination = NetworkShare.ToUncPath(_txtDest.Text.Trim());
            c.NasUser = _txtUser.Text.Trim();
            c.NasPassword = c.NasUser.Length > 0 ? ConfigService.EncryptPassword(_txtPass.Text) : string.Empty;
            c.Exclusions = _txtExclusions.Text.Trim();
            c.CopiesToKeep = (int)_numCopies.Value;

            c.Schedule = _rbDaily.Checked ? ScheduleMode.Daily : _rbWeekly.Checked ? ScheduleMode.Weekly : ScheduleMode.Disabled;
            c.WeeklyDays = new List<DayOfWeek>();
            foreach (DayToggle t in _days)
                if (t.Checked) c.WeeklyDays.Add(t.Day);
            if (c.WeeklyDays.Count > 0) c.WeeklyDay = c.WeeklyDays[0];
            c.ScheduleTime = _dtpTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture);

            c.ShutdownAfterBackup = _chkShutdown.Checked;
            c.ShutdownOnlyIfNoErrors = _chkOnlyOk.Checked;
            c.StartWithWindows = _chkStartup.Checked;
            c.WakeToRun = _chkWake.Checked;
            c.RunMissedBackup = _chkMissed.Checked;
            return c;
        }

        private bool SaveSettings(bool fromButton)
        {
            BackupConfig config = ReadConfigFromUi();

            if (config.Schedule == ScheduleMode.Weekly && config.WeeklyDays.Count == 0)
            {
                ShowPage(2);
                MessageBox.Show(this, "Marca al menos un día de la semana para la copia automática.", "Copyex Backup Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (config.Schedule != ScheduleMode.Disabled && (config.Sources.Count == 0 || string.IsNullOrWhiteSpace(config.Destination)))
            {
                ShowPage(1);
                MessageBox.Show(this, "Para programar la copia automática indica antes qué copiar y dónde guardarlo.", "Copyex Backup Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!string.Equals(config.Destination, _txtDest.Text.Trim(), StringComparison.Ordinal))
            {
                _loading = true;
                _txtDest.Text = config.Destination;
                _loading = false;
                AppendLog(BackupLogLevel.Info, "La unidad de red se ha cambiado por su ruta completa: " + config.Destination);
            }

            try
            {
                ConfigService.Save(config);
                _config = config;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo guardar la configuración.\r\n\r\n" + ex.Message, "Copyex Backup Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            List<string> warnings = new List<string>();
            try
            {
                StartupService.Apply(config.StartWithWindows);
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
            }

            Cursor = Cursors.WaitCursor;
            SchedulerResult sr = SchedulerService.Apply(config, true);
            Cursor = Cursors.Default;
            if (!sr.Success)
                warnings.Add(sr.Message);

            AppendLog(sr.Success ? BackupLogLevel.Success : BackupLogLevel.Warning, "Configuración guardada. " + sr.Message);

            if (warnings.Count > 0)
            {
                MessageBox.Show(this, "La configuración se ha guardado, pero:\r\n\r\n" + string.Join("\r\n\r\n", warnings.ToArray()),
                    "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetDirty(false, "Guardado con avisos");
            }
            else
            {
                SetDirty(false, fromButton ? "✓ Cambios guardados. " + Capitalize(sr.Message) : null);
            }

            RefreshHome();
            return true;
        }

        private void MarkDirty()
        {
            if (_loading) return;
            SetDirty(true, null);
        }

        private void SetDirty(bool dirty, string message)
        {
            _dirty = dirty;
            if (dirty)
            {
                _lblDirty.Text = "Hay cambios sin guardar";
                _lblDirty.ForeColor = Theme.Warning;
            }
            else
            {
                _lblDirty.Text = message ?? "Sin cambios pendientes";
                _lblDirty.ForeColor = message != null ? Theme.Success : Theme.Muted;
            }
        }

        private void UpdateScheduleUi()
        {
            bool on = !_rbOff.Checked;
            foreach (DayToggle t in _days) t.Enabled = _rbWeekly.Checked;
            _dtpTime.Enabled = on;
            _chkWake.Enabled = on;
            _chkMissed.Enabled = on;
            _chkOnlyOk.Enabled = _chkShutdown.Checked;
        }

        private void ChkShutdown_CheckedChanged(object sender, EventArgs e)
        {
            _chkOnlyOk.Enabled = _chkShutdown.Checked;
            if (_loading) return;
            _loading = true;
            _chkShutdownQuick.Checked = _chkShutdown.Checked;
            _loading = false;
            MarkDirty();
        }

        // La casilla rápida de Inicio se guarda al momento.
        private void ChkShutdownQuick_CheckedChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _loading = true;
            _chkShutdown.Checked = _chkShutdownQuick.Checked;
            _chkOnlyOk.Enabled = _chkShutdown.Checked;
            _loading = false;

            _config.ShutdownAfterBackup = _chkShutdownQuick.Checked;
            try
            {
                ConfigService.Save(_config);
            }
            catch
            {
            }
            AppendLog(BackupLogLevel.Info, _config.ShutdownAfterBackup
                ? "Se apagará el equipo al terminar la copia."
                : "No se apagará el equipo al terminar la copia.");
        }

        // ---------------------------------------------------------------- orígenes

        private void AddSource(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = path.Trim();
            foreach (ListViewItem existing in _lvSources.Items)
                if (string.Equals(existing.Text, path, StringComparison.OrdinalIgnoreCase)) return;

            ListViewItem item = new ListViewItem(path);
            bool isDir = FileSystem.DirectoryExists(path);
            bool isFile = !isDir && FileSystem.FileExists(path);
            item.SubItems.Add(isDir ? "Carpeta" : isFile ? "Archivo" : "—");
            item.SubItems.Add(isDir || isFile ? "Disponible" : "No encontrado");
            if (!isDir && !isFile) item.ForeColor = Theme.Danger;
            _lvSources.Items.Add(item);
            MarkDirty();
        }

        private void RemoveSelectedSources()
        {
            if (_lvSources.SelectedItems.Count == 0) return;
            while (_lvSources.SelectedItems.Count > 0)
                _lvSources.Items.Remove(_lvSources.SelectedItems[0]);
            MarkDirty();
        }

        private void BtnAddFolder_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Selecciona una carpeta para incluir en la copia";
                d.ShowNewFolderButton = false;
                if (d.ShowDialog(this) == DialogResult.OK)
                    AddSource(d.SelectedPath);
            }
        }

        private void BtnAddFiles_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Selecciona uno o varios archivos";
                d.Multiselect = true;
                if (d.ShowDialog(this) == DialogResult.OK)
                    foreach (string f in d.FileNames) AddSource(f);
            }
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Selecciona la carpeta del NAS donde guardar las copias. También puedes escribir la ruta de red (\\\\NAS\\carpeta).";
                d.ShowNewFolderButton = true;
                if (Directory.Exists(_txtDest.Text))
                    d.SelectedPath = _txtDest.Text;
                if (d.ShowDialog(this) == DialogResult.OK)
                    _txtDest.Text = NetworkShare.ToUncPath(d.SelectedPath);
            }
        }

        private void BtnTest_Click(object sender, EventArgs e)
        {
            string dest = NetworkShare.ToUncPath(_txtDest.Text.Trim());
            string user = _txtUser.Text.Trim();
            string pass = _txtPass.Text;
            if (dest.Length == 0)
            {
                MessageBox.Show(this, "Escribe o selecciona primero la carpeta de destino.", "Copyex Backup Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _btnTest.Enabled = false;
            _btnTest.Text = "Probando...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string message;
                bool ok;
                try
                {
                    string loginError = user.Length > 0 ? NetworkShare.Connect(dest, user, pass) : null;
                    BackupService.TestDestination(dest);
                    long free, total;
                    message = "Conexión correcta: se puede escribir en el destino.";
                    if (FileSystem.TryGetFreeSpace(dest, out free, out total))
                        message += "\r\n\r\nEspacio libre: " + FileSystem.FormatBytes(free) + " de " + FileSystem.FormatBytes(total) + ".";
                    if (loginError != null)
                        message += "\r\n\r\nAviso sobre el usuario del NAS: " + loginError;
                    ok = true;
                }
                catch (Exception ex)
                {
                    message = "No se puede escribir en el destino.\r\n\r\n" + ex.Message;
                    ok = false;
                }

                SafeInvoke(delegate
                {
                    _btnTest.Enabled = true;
                    _btnTest.Text = "Probar conexión";
                    MessageBox.Show(this, message, "Copyex Backup Agent", MessageBoxButtons.OK,
                        ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                });
            });
        }

        // =====================================================================================
        // Copia
        // =====================================================================================

        private void BtnRun_Click(object sender, EventArgs e)
        {
            if (_runner.IsBusy)
            {
                if (MessageBox.Show(this, "¿Cancelar la copia en curso? Se conservarán las copias anteriores.", "Copyex Backup Agent",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                _runner.Cancel();
                _btnRun.Enabled = false;
                _lblRunState.Text = "Cancelando...";
                return;
            }

            if (BackupRunner.IsBackupRunningElsewhere())
            {
                MessageBox.Show(this, "Ya hay una copia de seguridad en curso (la programada). Espera a que termine.",
                    "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_lvSources.Items.Count == 0)
            {
                ShowPage(1);
                MessageBox.Show(this, "Añade al menos una carpeta para copiar.", "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_txtDest.Text.Trim().Length == 0)
            {
                ShowPage(1);
                MessageBox.Show(this, "Indica dónde guardar las copias.", "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_dirty && !SaveSettings(false))
                return;

            AppendLog(BackupLogLevel.Info, "──────────── Inicio de copia manual ────────────");
            SetRunningUi(true);
            _runner.Start(_config, false);
        }

        private void Runner_Progress(BackupProgress p)
        {
            switch (p.Phase)
            {
                case BackupPhase.Preparing:
                    _lblRunState.Text = "Conectando con el destino...";
                    _progress.Marquee = true;
                    break;
                case BackupPhase.Scanning:
                    _lblRunState.Text = "Analizando archivos...";
                    _progress.Marquee = true;
                    _lblRunStats.Text = p.FilesTotal.ToString("N0") + " archivos encontrados (" + FileSystem.FormatBytes(p.BytesTotal) + ")";
                    break;
                case BackupPhase.Copying:
                    int pct = (int)(p.Fraction * 100);
                    _progress.Marquee = false;
                    _progress.Value = p.Fraction;
                    _lblRunState.Text = "Copiando... " + pct + " %";
                    _lblRunStats.Text = FormatProgress(p);
                    _lblRunSpeed.Text = FormatSpeed(p);
                    SetTrayText("Copiando " + pct + " %");
                    break;
                case BackupPhase.Finishing:
                    _progress.Marquee = true;
                    _lblRunState.Text = "Finalizando y limpiando copias antiguas...";
                    break;
            }
            _lblRunFile.Text = p.CurrentItem;
        }

        private void Runner_Completed(BackupResult r)
        {
            SetRunningUi(false);
            _lblRunStats.Text = r.Message;
            _lblRunSpeed.Text = r.BackupFolder.Length > 0 ? "Guardada en " + r.BackupFolder : string.Empty;
            _lblRunFile.Text = string.Empty;
            _progress.Value = r.Status == BackupStatus.Ok || r.Status == BackupStatus.CompletedWithErrors ? 1 : 0;
            _lblRunState.Text = StatusTitle(r.Status);
            _lblRunState.ForeColor = StatusColor(r.Status);
            SetTrayText(null);

            if (!Visible && _tray != null)
            {
                _tray.ShowBalloonTip(6000, StatusTitle(r.Status), r.Message,
                    r.Status == BackupStatus.Ok ? ToolTipIcon.Info : r.Status == BackupStatus.Cancelled ? ToolTipIcon.None : ToolTipIcon.Warning);
            }

            RefreshHome();
            RefreshRuns();

            if (_exitAfterRun)
            {
                _allowExit = true;
                if (_tray != null) _tray.Visible = false;
                Close();
                return;
            }

            if (SystemPower.ShouldShutdown(_config, r))
            {
                using (ShutdownForm f = new ShutdownForm(r.Message))
                    f.ShowDialog();
            }
        }

        private void SetRunningUi(bool running)
        {
            bool blocked = running || _externalRunning;
            _btnRun.Enabled = running || !_externalRunning;
            _btnRun.Text = running ? "Cancelar copia" : "Hacer copia ahora";
            _btnRun.Kind = running ? ButtonKind.Secondary : ButtonKind.Primary;
            _btnRun.Invalidate();
            _trayRun.Enabled = !blocked;

            if (running)
            {
                _lblRunState.ForeColor = Theme.Text;
                _lblRunState.Text = "Preparando la copia...";
                _lblRunStats.Text = string.Empty;
                _lblRunSpeed.Text = string.Empty;
                _lblRunFile.Text = string.Empty;
                _progress.Value = 0;
                _progress.Marquee = true;
            }
            else
            {
                _progress.Marquee = false;
                if (_externalRunning)
                {
                    _lblRunState.ForeColor = Theme.Text;
                    _lblRunState.Text = "La copia programada está en curso en su propia ventana.";
                }
            }
            UpdateHeaderStatus();
        }

        private void UpdateHeaderStatus()
        {
            if (_runner.IsBusy || _externalRunning)
            {
                _lblHeaderStatus.Text = "● Copia en curso";
                _lblHeaderStatus.BackColor = Theme.PrimarySoft;
                _lblHeaderStatus.ForeColor = Theme.Primary;
                return;
            }

            BackupRecord last = HistoryService.GetLast();
            if (last != null && (last.Status == BackupStatus.Failed || last.Status == BackupStatus.CompletedWithErrors))
            {
                _lblHeaderStatus.Text = "● Revisar última copia";
                _lblHeaderStatus.BackColor = Theme.WarningSoft;
                _lblHeaderStatus.ForeColor = Theme.Warning;
            }
            else
            {
                _lblHeaderStatus.Text = "● Agente activo";
                _lblHeaderStatus.BackColor = Theme.SuccessSoft;
                _lblHeaderStatus.ForeColor = Theme.Success;
            }
        }

        private void SetTrayText(string text)
        {
            if (_tray == null) return;
            string value = "Copyex Backup Agent" + (string.IsNullOrEmpty(text) ? string.Empty : " - " + text);
            _tray.Text = value.Length > 63 ? value.Substring(0, 63) : value;
        }

        // =====================================================================================
        // Datos de inicio e historial
        // =====================================================================================

        private void RefreshHome()
        {
            BackupRecord last = HistoryService.GetLast();
            if (last == null)
            {
                _lblLastValue.Text = "Sin copias";
                _lblLastSub.Text = "Pulsa «Hacer copia ahora» para la primera";
                _lblLastSub.ForeColor = Theme.Muted;
            }
            else
            {
                _lblLastValue.Text = FormatWhen(last.Start);
                string detail = StatusTitle(last.Status);
                if (last.Files > 0)
                    detail += " · " + last.Files.ToString("N0") + " archivos · " + FileSystem.FormatBytes(last.Bytes);
                _lblLastSub.Text = detail;
                _lblLastSub.ForeColor = StatusColor(last.Status);
            }

            if (!_runner.IsBusy && !_externalRunning && _lblRunState.Text == "Lista para copiar")
            {
                int n = _config.Sources.Count;
                _lblRunStats.Text = n == 0
                    ? "Añade en «Origen y destino» las carpetas que quieres proteger."
                    : "Se copiará " + (n == 1 ? "1 elemento" : n + " elementos") + " en el destino. Las copias anteriores se conservan hasta " + _config.CopiesToKeep + ".";
                _lblRunSpeed.Text = _config.ShutdownAfterBackup ? "Al terminar se apagará el equipo (con aviso previo de 60 segundos)." : string.Empty;
            }

            UpdateNextRunLabels();
            _lblDestValue.Text =string.IsNullOrWhiteSpace(_config.Destination) ? "Sin configurar" : _config.Destination;
            UpdateHeaderStatus();
            RefreshDestinationAsync();
        }

        private void UpdateNextRunLabels()
        {
            DateTime? next = SchedulerService.GetNextRun(_config, DateTime.Now);
            if (next.HasValue)
            {
                _lblNextValue.Text = FormatWhen(next.Value);
                _lblNextSub.Text = Capitalize(SchedulerService.Describe(_config)) +
                    (_config.ShutdownAfterBackup ? " · y apagar" : string.Empty);
                _lblSidebarNext.Text = FormatWhen(next.Value);
            }
            else
            {
                _lblNextValue.Text = "No programada";
                _lblNextSub.Text = "Actívala en Programación";
                _lblSidebarNext.Text = "No programada";
            }
        }

        private void RefreshDestinationAsync()
        {
            BackupConfig cfg = _config.Clone();
            int id = Interlocked.Increment(ref _destRefreshId);

            if (string.IsNullOrWhiteSpace(cfg.Destination))
            {
                _lblStoredValue.Text = "—";
                _lblStoredSub.Text = "Configura el destino";
                _lblStoredSub.ForeColor = Theme.Muted;
                _lvBackups.Items.Clear();
                _lblBackupsInfo.Text = "No hay destino configurado.";
                return;
            }

            _lblBackupsInfo.Text = "Consultando " + cfg.Destination + "...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<BackupFolderInfo> list = null;
                string error = null;
                long free = -1, total;
                try
                {
                    if (!string.IsNullOrWhiteSpace(cfg.NasUser))
                        NetworkShare.Connect(cfg.Destination, cfg.NasUser, ConfigService.DecryptPassword(cfg.NasPassword));
                    if (FileSystem.DirectoryExists(cfg.Destination))
                    {
                        list = BackupService.ListBackups(cfg.Destination);
                        if (!FileSystem.TryGetFreeSpace(cfg.Destination, out free, out total))
                            free = -1;
                    }
                    else
                    {
                        error = "No se encuentra la carpeta, o el NAS pide usuario y contraseña. Se creará en la primera copia si hay acceso.";
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                SafeInvoke(delegate
                {
                    if (id != _destRefreshId) return;
                    ShowDestinationInfo(cfg, list, free, error);
                });
            });
        }

        private void ShowDestinationInfo(BackupConfig cfg, List<BackupFolderInfo> list, long free, string error)
        {
            _lvBackups.BeginUpdate();
            _lvBackups.Items.Clear();

            if (error != null || list == null)
            {
                _lblStoredValue.Text = "No accesible";
                _lblStoredSub.Text = error ?? "No se pudo leer el destino";
                _lblStoredSub.ForeColor = Theme.Danger;
                _lblBackupsInfo.Text = "No se puede acceder al destino: " + error;
                _lvBackups.EndUpdate();
                return;
            }

            int own = 0;
            long ownBytes = 0;
            foreach (BackupFolderInfo b in list)
            {
                if (b.IsOwn)
                {
                    own++;
                    if (b.Bytes > 0) ownBytes += b.Bytes;
                }

                ListViewItem item = new ListViewItem(b.Date.ToString("dd/MM/yyyy  HH:mm", Es));
                item.SubItems.Add(string.IsNullOrEmpty(b.Machine) ? "—" : b.Machine);
                item.SubItems.Add(b.Files >= 0 ? b.Files.ToString("N0") : "—");
                item.SubItems.Add(b.Bytes >= 0 ? FileSystem.FormatBytes(b.Bytes) : "—");
                item.SubItems.Add(b.HasMarker ? (b.Errors > 0 ? b.Errors.ToString("N0") : "Ninguna") : "—");
                item.SubItems.Add(b.Name);
                item.Tag = b;
                if (!b.IsOwn) item.ForeColor = Theme.Muted;
                else if (b.Errors > 0) item.ForeColor = Theme.Warning;
                _lvBackups.Items.Add(item);
            }
            _lvBackups.EndUpdate();
            FillLastColumn(_lvBackups);

            _lblStoredValue.Text = own + " de " + cfg.CopiesToKeep;
            _lblStoredSub.Text = free >= 0 ? "Libre en destino: " + FileSystem.FormatBytes(free) : "Espacio libre desconocido";
            _lblStoredSub.ForeColor = Theme.Muted;
            _lblBackupsInfo.Text = own + " copias de este equipo" + (ownBytes > 0 ? " (" + FileSystem.FormatBytes(ownBytes) + ")" : string.Empty) +
                ". Se conservan las " + cfg.CopiesToKeep + " más recientes." +
                (list.Count > own ? " En gris, copias de otros equipos (no se tocan)." : string.Empty);
        }

        private void RefreshRuns()
        {
            List<BackupRecord> runs = HistoryService.Load();
            _lvRuns.BeginUpdate();
            _lvRuns.Items.Clear();
            foreach (BackupRecord r in runs)
            {
                ListViewItem item = new ListViewItem(r.Start.ToString("dd/MM/yyyy  HH:mm", Es));
                item.SubItems.Add(r.Scheduled ? "Programada" : "Manual");
                item.SubItems.Add(StatusTitle(r.Status));
                item.SubItems.Add(r.Files > 0 ? r.Files.ToString("N0") : "—");
                item.SubItems.Add(r.Bytes > 0 ? FileSystem.FormatBytes(r.Bytes) : "—");
                item.SubItems.Add(r.End > r.Start ? BackupService.FormatDuration(r.End - r.Start) : "—");
                item.SubItems.Add(r.Message);
                item.ForeColor = StatusColor(r.Status);
                item.ToolTipText = r.Message;
                _lvRuns.Items.Add(item);
            }
            _lvRuns.ShowItemToolTips = true;
            _lvRuns.EndUpdate();
            FillLastColumn(_lvRuns);
        }

        private BackupFolderInfo SelectedBackup()
        {
            if (_lvBackups.SelectedItems.Count == 0) return null;
            return _lvBackups.SelectedItems[0].Tag as BackupFolderInfo;
        }

        private void BtnDeleteBackup_Click(object sender, EventArgs e)
        {
            BackupFolderInfo info = SelectedBackup();
            if (info == null)
            {
                MessageBox.Show(this, "Selecciona primero una copia de la lista.", "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_runner.IsBusy || _externalRunning)
            {
                MessageBox.Show(this, "Espera a que termine la copia en curso.", "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "¿Eliminar definitivamente la copia del " + info.Date.ToString("dd/MM/yyyy HH:mm", Es) + "?\r\n\r\n" + info.Path,
                    "Eliminar copia", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _lblBackupsInfo.Text = "Eliminando " + info.Name + "...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    FileSystem.DeleteTree(info.Path);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                SafeInvoke(delegate
                {
                    if (error != null)
                    {
                        AppendLog(BackupLogLevel.Error, "No se pudo eliminar " + info.Name + ": " + error);
                        MessageBox.Show(this, "No se pudo eliminar la copia.\r\n\r\n" + error, "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        AppendLog(BackupLogLevel.Info, "Copia eliminada manualmente: " + info.Name);
                    }
                    RefreshDestinationAsync();
                });
            });
        }

        // =====================================================================================
        // Registro
        // =====================================================================================

        private void AppendLog(BackupLogLevel level, string message)
        {
            AppendLog(level, message, DateTime.Now);
        }

        private void AppendLog(BackupLogLevel level, string message, DateTime timestamp)
        {
            if (_rtbLog == null || _rtbLog.IsDisposed) return;

            if (_logLines >= MaxLogLines)
            {
                int cut = _rtbLog.GetFirstCharIndexFromLine(500);
                if (cut > 0)
                {
                    _rtbLog.Select(0, cut);
                    _rtbLog.ReadOnly = false;
                    _rtbLog.SelectedText = string.Empty;
                    _rtbLog.ReadOnly = true;
                    _logLines -= 500;
                }
            }

            string tag;
            Color color;
            switch (level)
            {
                case BackupLogLevel.Success: tag = "OK   "; color = Theme.Success; break;
                case BackupLogLevel.Warning: tag = "AVISO"; color = Theme.Warning; break;
                case BackupLogLevel.Error: tag = "ERROR"; color = Theme.Danger; break;
                default: tag = "INFO "; color = Color.FromArgb(40, 90, 160); break;
            }

            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = Theme.Muted;
            _rtbLog.AppendText(timestamp.ToString("dd/MM HH:mm:ss") + "  ");
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText(tag + "  ");
            _rtbLog.SelectionColor = level == BackupLogLevel.Error ? Theme.Danger : Theme.Text;
            _rtbLog.AppendText((message ?? string.Empty) + Environment.NewLine);
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.ScrollToCaret();
            _logLines++;
        }

        private void BtnSaveLog_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Texto (*.txt)|*.txt";
                d.FileName = "CopyexBackup_registro_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".txt";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, _rtbLog.Text, System.Text.Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // =====================================================================================
        // Utilidades
        // =====================================================================================

        internal static string FormatProgress(BackupProgress p)
        {
            return p.FilesDone.ToString("N0") + " de " + p.FilesTotal.ToString("N0") + " archivos  ·  " +
                   FileSystem.FormatBytes(p.BytesDone) + " de " + FileSystem.FormatBytes(p.BytesTotal);
        }

        private static string FormatSpeed(BackupProgress p)
        {
            if (p.BytesPerSecond <= 0) return "Calculando velocidad...";
            string text = FileSystem.FormatBytes(p.BytesPerSecond) + "/s";
            if (p.HasRemaining)
            {
                TimeSpan t = p.Remaining;
                string rest = t.TotalMinutes < 1 ? "menos de 1 min" :
                              t.TotalHours < 1 ? (int)Math.Ceiling(t.TotalMinutes) + " min" :
                              (int)t.TotalHours + " h " + t.Minutes + " min";
                text += "  ·  quedan unos " + rest;
            }
            return text;
        }

        private static string FormatWhen(DateTime d)
        {
            DateTime today = DateTime.Today;
            string hour = d.ToString("HH:mm");
            if (d.Date == today) return "Hoy, " + hour;
            if (d.Date == today.AddDays(1)) return "Mañana, " + hour;
            if (d.Date == today.AddDays(-1)) return "Ayer, " + hour;
            return Capitalize(d.ToString("ddd d MMM", Es).Replace(".", string.Empty)) + ", " + hour;
        }

        private static string StatusTitle(BackupStatus s)
        {
            switch (s)
            {
                case BackupStatus.Ok: return "Copia correcta";
                case BackupStatus.CompletedWithErrors: return "Con incidencias";
                case BackupStatus.Cancelled: return "Cancelada";
                case BackupStatus.Skipped: return "Omitida (otra en curso)";
                default: return "Fallida";
            }
        }

        private static Color StatusColor(BackupStatus s)
        {
            switch (s)
            {
                case BackupStatus.Ok: return Theme.Success;
                case BackupStatus.CompletedWithErrors: return Theme.Warning;
                case BackupStatus.Failed: return Theme.Danger;
                default: return Theme.Muted;
            }
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0], Es) + s.Substring(1);
        }

        private void OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir la carpeta.\r\n\r\n" + ex.Message, "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SafeInvoke(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _monitor.Dispose();
                _runner.Dispose();
                if (_tray != null) _tray.Dispose();
                if (_trayMenu != null) _trayMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
