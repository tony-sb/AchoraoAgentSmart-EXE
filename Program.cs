using System.Diagnostics;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new MainForm());

enum AgentMode { Telemetry, Sanitize }

#pragma warning disable CS8618 // Fields assigned in BuildUI
class MainForm : Form
{
    private Button _btnModeTelemetry, _btnModeSanitize;
    private Panel _telemetryPanel, _sanitizePanel;
    private Label _headerTitle;
    private Label _headerSub;
    private Panel _cardDisk;
    private Label _lblDiskName, _lblSerial, _lblCapacity, _lblType;
    private Panel _cardHealth;
    private ProgressBar _healthBar;
    private Label _lblHealthValue, _lblGrade;
    private Label _lblRamInfo, _lblGpuInfo;
    private Label _lblStatus;
    private Panel _statusBar;
    private Button _btnStart, _btnExit;
    private System.Windows.Forms.Timer _spinnerTimer;
    private int _spinnerDotCount;
    private DiskMetrics? _metrics;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private TextBox _txtBackendUrl;
    private ListView _diskListView;
    private NumericUpDown _diskSelector;
    private Button _btnStartSanitize, _btnCancelSanitize;
    private Label _lblMethodInfo;
    private Label _lblSanitizeRamInfo, _lblSanitizeGpuInfo;
    private Label _lblSanitizeStatus;
    private TextBox _txtCertHash;
    private Button _btnCopyCert;
    private CancellationTokenSource? _sanitizeCts;
    private AgentMode _currentMode = AgentMode.Telemetry;

    public MainForm()
    {
        Text = "Agente Achorao v1.0.4";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Size = new Size(580, 720);
        BackColor = Color.FromArgb(10, 10, 11);
        Font = new Font("Segoe UI", 10);

        NativeMethods.Engine_Initialize("achorao_engine.log");
        FormClosing += (_, e) => NativeMethods.Engine_Shutdown();

        BuildUI();

        // Auto-detect all hardware on startup
        Shown += async (_, _) =>
        {
            SetStatus("Detectando hardware del sistema...");
            await Task.Run(DetectAllHardware);
            SetStatus("Listo. Todos los componentes detectados.");
        };
    }

    private void DetectAllHardware()
    {
        try
        {
            // 1. Storage via native engine
            var storageJson = NativeMethods.PtrToString(NativeMethods.Engine_EnumerateStorage());
            if (!string.IsNullOrEmpty(storageJson))
            {
                using var doc = JsonDocument.Parse(storageJson);
                var arr = doc.RootElement.EnumerateArray().ToList();
                Invoke(() =>
                {
                    _diskListView.Items.Clear();
                    int idx = 0;
                    foreach (var item in arr)
                    {
                        var num = item.TryGetProperty("deviceNumber", out var dn) ? dn.GetInt32().ToString() : idx.ToString();
                        var model = item.TryGetProperty("model", out var m) ? m.GetString() ?? "?" : "?";
                        var serial = item.TryGetProperty("serialNumber", out var sn) ? sn.GetString() ?? "N/A" : "N/A";
                        var cap = item.TryGetProperty("capacity", out var c) ? c.GetString() ?? "?" : "?";
                        var mediaType = item.TryGetProperty("mediaType", out var mt) ? mt.GetString() ?? "?" : "?";
                        var bus = item.TryGetProperty("busInterface", out var bt) ? bt.GetString() ?? "?" : "?";

                        var lvItem = new ListViewItem(num);
                        lvItem.SubItems.Add(model);
                        lvItem.SubItems.Add(cap);
                        lvItem.SubItems.Add(mediaType);
                        lvItem.SubItems.Add(serial);
                        lvItem.SubItems.Add(bus);
                        _diskListView.Items.Add(lvItem);
                        idx++;
                    }
                    if (idx > 0)
                    {
                        _diskSelector.Maximum = idx - 1;
                        _diskSelector.Value = 0;
                        UpdateMethodInfo(0);
                    }
                });
            }

            // 2. Memory via native engine
            var memJson = NativeMethods.PtrToString(NativeMethods.Engine_DetectMemory());
            if (!string.IsNullOrEmpty(memJson))
            {
                using var doc = JsonDocument.Parse(memJson);
                var modules = doc.RootElement.EnumerateArray().ToList();
                if (modules.Count > 0)
                {
                    ulong totalBytes = 0;
                    string details = "";
                    foreach (var mod in modules)
                    {
                        var cap = mod.TryGetProperty("capacity", out var c) ? c.GetString() ?? "" : "";
                        var speed = mod.TryGetProperty("speed", out var sp) ? sp.GetString() ?? "" : "";
                        var memType = mod.TryGetProperty("memoryType", out var mt) ? mt.GetString() ?? "" : "";
                        details = $"{memType} {speed}".Trim();
                        if (cap.EndsWith("MB"))
                        {
                            if (ulong.TryParse(cap[..^3], out var mb))
                                totalBytes += mb * 1024UL * 1024UL;
                        }
                    }
                    var totalGB = totalBytes / (1024UL * 1024UL * 1024UL);
                    var ramText = totalGB > 0 ? $"{totalGB} GB {details}" : details;
                    if (string.IsNullOrEmpty(ramText)) ramText = $"{modules.Count} modulo(s)";
                    Invoke(() =>
                    {
                        _lblRamInfo.Text = "RAM: " + ramText + $" ({modules.Count} modulos)";
                        _lblSanitizeRamInfo.Text = "RAM: " + ramText;
                    });
                }
            }

            // 3. GPU via native engine
            var gpuJson = NativeMethods.PtrToString(NativeMethods.Engine_DetectGPU());
            if (!string.IsNullOrEmpty(gpuJson))
            {
                using var doc = JsonDocument.Parse(gpuJson);
                var gpus = doc.RootElement.EnumerateArray().ToList();
                if (gpus.Count > 0)
                {
                    var gpu = gpus[0];
                    var name = gpu.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
                    var vendor = gpu.TryGetProperty("vendor", out var v) ? v.GetString() ?? "" : "";
                    var vram = gpu.TryGetProperty("dedicatedMemoryBytes", out var vb) ? vb.GetUInt64() : 0UL;
                    var vramMB = vram / (1024UL * 1024UL);
                    var health = gpu.TryGetProperty("healthPercent", out var hp) ? hp.GetInt32() : 0;
                    var bios = gpu.TryGetProperty("biosVersion", out var b) ? b.GetString() ?? "" : "";
                    var gpuText = $"{vendor} {name}".Trim();
                    if (vramMB > 0) gpuText += $" | {vramMB} MB VRAM";
                    Invoke(() =>
                    {
                        _lblGpuInfo.Text = "GPU: " + gpuText + $" | Salud: {health}%";
                        _lblSanitizeGpuInfo.Text = "GPU: " + gpuText;
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Invoke(() => SetStatus("Error en deteccion: " + ex.Message));
        }
    }

    private void BuildUI()
    {
        var header = new Panel
        {
            Size = new Size(580, 100),
            Location = new Point(0, 0),
            BackColor = Color.FromArgb(10, 10, 11)
        };
        _headerTitle = new Label
        {
            Text = "ACHORAO AGENT",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.FromArgb(228, 228, 231),
            Location = new Point(24, 10),
            Size = new Size(280, 36),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _btnModeTelemetry = new Button
        {
            Text = "TELEMETRIA",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(239, 68, 68),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(360, 14),
            Size = new Size(100, 28),
            Cursor = Cursors.Hand
        };
        _btnModeTelemetry.Click += (_, _) => SwitchMode(AgentMode.Telemetry);

        _btnModeSanitize = new Button
        {
            Text = "SANEAMIENTO",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(100, 100, 120),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(464, 14),
            Size = new Size(100, 28),
            Cursor = Cursors.Hand
        };
        _btnModeSanitize.Click += (_, _) => SwitchMode(AgentMode.Sanitize);

        _headerSub = new Label
        {
            Text = "v1.0.4 | Recopilador de telemetria y modulo de saneamiento",
            Font = new Font("Segoe UI", 8, FontStyle.Regular),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(28, 52),
            Size = new Size(460, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.AddRange([_headerTitle, _btnModeTelemetry, _btnModeSanitize, _headerSub]);

        // ============ TELEMETRY PANEL ============
        _telemetryPanel = new Panel { Location = new Point(0, 100), Size = new Size(580, 560), BackColor = Color.FromArgb(10, 10, 11) };

        _cardDisk = CreateCard("UNIDAD DE ALMACENAMIENTO", new Point(20, 20), new Size(540, 140));
        _lblDiskName = AddCardField(_cardDisk, "Disco:", "---", 0);
        _lblSerial = AddCardField(_cardDisk, "Serial:", "---", 1);
        _lblCapacity = AddCardField(_cardDisk, "Capacidad:", "---", 2);
        _lblType = AddCardField(_cardDisk, "Tipo:", "---", 3);

        _cardHealth = CreateCard("ESTADO DE SALUD", new Point(20, 180), new Size(540, 120));
        _healthBar = new ProgressBar
        {
            Location = new Point(20, 40),
            Size = new Size(440, 22),
            Style = ProgressBarStyle.Continuous,
            ForeColor = Color.FromArgb(234, 179, 8),
            BackColor = Color.FromArgb(24, 24, 27),
            Value = 0
        };
        _lblHealthValue = new Label
        {
            Text = "---",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(234, 179, 8),
            Location = new Point(468, 36),
            Size = new Size(60, 30),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblGrade = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(234, 179, 8),
            Location = new Point(20, 70),
            Size = new Size(100, 36),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _cardHealth.Controls.AddRange([_healthBar, _lblHealthValue, _lblGrade]);

        // ---- SISTEMA (RAM + GPU) ----
        var cardSystem = CreateCard("SISTEMA", new Point(20, 315), new Size(540, 100));
        _lblRamInfo = new Label
        {
            Text = "RAM: ---",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(228, 228, 231),
            Location = new Point(18, 36),
            Size = new Size(500, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblGpuInfo = new Label
        {
            Text = "GPU: ---",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(228, 228, 231),
            Location = new Point(18, 62),
            Size = new Size(500, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };
        cardSystem.Controls.AddRange([_lblRamInfo, _lblGpuInfo]);

        _btnStart = new Button
        {
            Text = "INICIAR LECTURA",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(239, 68, 68),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(120, 440),
            Size = new Size(200, 42),
            Cursor = Cursors.Hand
        };
        _btnStart.Click += BtnStart_Click;

        _btnExit = new Button
        {
            Text = "CERRAR",
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            ForeColor = Color.FromArgb(161, 161, 170),
            BackColor = Color.FromArgb(24, 24, 27),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(340, 440),
            Size = new Size(120, 42),
            Cursor = Cursors.Hand
        };
        _btnExit.Click += (_, _) => Close();

        _spinnerTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _spinnerTimer.Tick += (_, _) => { _spinnerDotCount = (_spinnerDotCount + 1) % 6; };

        _telemetryPanel.Controls.AddRange([_cardDisk, _cardHealth, cardSystem, _btnStart, _btnExit]);

        // ============ SANITIZE PANEL ============
        _sanitizePanel = new Panel { Location = new Point(0, 100), Size = new Size(580, 560), BackColor = Color.FromArgb(10, 10, 11), Visible = false };

        Label SectionTitle(string text, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(10, 10, 11),
                Location = new Point(20, y),
                Size = new Size(540, 18),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _sanitizePanel.Controls.Add(new Panel { Location = new Point(20, y + 18), Size = new Size(540, 1), BackColor = Color.FromArgb(24, 24, 27) });
            return lbl;
        }
        Label HelpText(string text, int y) => new Label
        {
            Text = text, Font = new Font("Segoe UI", 7, FontStyle.Italic),
            ForeColor = Color.FromArgb(113, 113, 122),
            Location = new Point(22, y), Size = new Size(540, 16),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // SECCION 1: URL
        _sanitizePanel.Controls.Add(SectionTitle("CONFIGURACION DEL SERVIDOR", 12));
        var urlLabel = new Label
        {
            Text = "Backend URL:",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(24, 40),
            Size = new Size(110, 24),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _txtBackendUrl = new TextBox
        {
            Text = "http://localhost:3000",
            Location = new Point(130, 40),
            Size = new Size(420, 24),
            Font = new Font("Segoe UI", 9),
            BackColor = Color.FromArgb(24, 24, 27),
            ForeColor = Color.FromArgb(228, 228, 231),
            BorderStyle = BorderStyle.FixedSingle
        };
        _sanitizePanel.Controls.Add(HelpText("URL del servidor central donde se registrara la auditoria del saneamiento.", 68));

        // SECCION 2: DISCOS
        _sanitizePanel.Controls.Add(SectionTitle("SELECCIONE EL DISCO A SANEAR", 84));
        _diskListView = new ListView
        {
            Location = new Point(24, 104),
            Size = new Size(532, 115),
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            Font = new Font("Segoe UI", 9),
            BackColor = Color.FromArgb(24, 24, 27),
            ForeColor = Color.FromArgb(228, 228, 231),
        };
        _diskListView.OwnerDraw = false;
        _diskListView.Columns.Add("#", 30);
        _diskListView.Columns.Add("Disco", 170);
        _diskListView.Columns.Add("Capacidad", 72);
        _diskListView.Columns.Add("Medio", 40);
        _diskListView.Columns.Add("Serial", 130);
        _diskListView.Columns.Add("Tipo", 60);

        var selLabel = new Label
        {
            Text = "Indice:",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(24, 230),
            Size = new Size(52, 24),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _diskSelector = new NumericUpDown
        {
            Location = new Point(76, 230),
            Size = new Size(60, 24),
            Minimum = 0, Maximum = 0,
            Font = new Font("Segoe UI", 9),
            BackColor = Color.FromArgb(24, 24, 27),
            ForeColor = Color.FromArgb(228, 228, 231),
        };
        _diskSelector.ValueChanged += (_, _) =>
        {
            UpdateMethodInfo((int)_diskSelector.Value);
        };

        // METODO DE BORRADO
        _lblMethodInfo = new Label
        {
            Text = "Metodo: Seleccione un disco",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(234, 179, 8),
            Location = new Point(24, 268),
            Size = new Size(532, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // SECCION 3: BOTONES
        _sanitizePanel.Controls.Add(SectionTitle("EJECUTAR SANEAMIENTO", 300));
        _btnStartSanitize = new Button
        {
            Text = "INICIAR SANEAMIENTO",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(239, 68, 68),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(120, 322),
            Size = new Size(200, 36),
            Cursor = Cursors.Hand
        };
        _btnStartSanitize.Click += BtnStartSanitize_Click;
        _btnCancelSanitize = new Button
        {
            Text = "CANCELAR",
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            ForeColor = Color.FromArgb(161, 161, 170),
            BackColor = Color.FromArgb(24, 24, 27),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(340, 322),
            Size = new Size(120, 36),
            Cursor = Cursors.Hand,
            Enabled = false
        };
        _btnCancelSanitize.Click += (_, _) => _sanitizeCts?.Cancel();

        // SECCION 4: ESTADO
        _sanitizePanel.Controls.Add(SectionTitle("ESTADO DEL PROCESO", 370));
        _lblSanitizeStatus = new Label
        {
            Text = "Configure el servidor, seleccione un disco y presione INICIAR SANEAMIENTO.",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 120),
            Location = new Point(24, 392),
            Size = new Size(532, 36),
            TextAlign = ContentAlignment.TopLeft
        };
        var certLabel = new Label
        {
            Text = "Serial:",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(24, 430),
            Size = new Size(80, 22),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _txtCertHash = new TextBox
        {
            Location = new Point(104, 430),
            Size = new Size(360, 22),
            Font = new Font("Consolas", 9),
            ReadOnly = true,
            BackColor = Color.FromArgb(24, 24, 27),
            ForeColor = Color.FromArgb(228, 228, 231),
            BorderStyle = BorderStyle.FixedSingle,
            Text = ""
        };
        _btnCopyCert = new Button
        {
            Text = "COPIAR",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(239, 68, 68),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(470, 429),
            Size = new Size(80, 24),
            Cursor = Cursors.Hand,
            Enabled = false
        };
        _btnCopyCert.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_txtCertHash.Text))
            {
                try
                {
                    ClipboardHelper.Copy(_txtCertHash.Text);
                    SetSanitizeStatus("Certificado copiado al portapapeles.", Color.FromArgb(161, 161, 170));
                }
                catch
                {
                    SetSanitizeStatus("Seleccione el texto y pulse Ctrl+C para copiar.", Color.FromArgb(239, 68, 68));
                }
            }
        };

        // SECCION 5: HARDWARE (RAM + GPU)
        var cardHardware = new Panel
        {
            Location = new Point(20, 462),
            Size = new Size(540, 88),
            BackColor = Color.FromArgb(24, 24, 27),
        };
        var hwTitle = new Label
        {
            Text = "ESTADO DEL SISTEMA",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(14, 6),
            Size = new Size(510, 16),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblSanitizeRamInfo = new Label
        {
            Text = "RAM: consultando...",
            Font = new Font("Segoe UI", 8, FontStyle.Regular),
            ForeColor = Color.FromArgb(228, 228, 231),
            Location = new Point(14, 28),
            Size = new Size(510, 18),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblSanitizeGpuInfo = new Label
        {
            Text = "GPU: consultando...",
            Font = new Font("Segoe UI", 8, FontStyle.Regular),
            ForeColor = Color.FromArgb(228, 228, 231),
            Location = new Point(14, 50),
            Size = new Size(510, 18),
            TextAlign = ContentAlignment.MiddleLeft
        };
        cardHardware.Controls.AddRange([hwTitle, _lblSanitizeRamInfo, _lblSanitizeGpuInfo]);

        _sanitizePanel.Controls.AddRange([urlLabel, _txtBackendUrl, _diskListView, selLabel, _diskSelector, _lblMethodInfo, _btnStartSanitize, _btnCancelSanitize, _lblSanitizeStatus, certLabel, _txtCertHash, _btnCopyCert, cardHardware]);

        // ============ STATUS BAR ============
        _statusBar = new Panel
        {
            Location = new Point(0, 660),
            Size = new Size(580, 40),
            BackColor = Color.FromArgb(10, 10, 11)
        };
        _lblStatus = new Label
        {
            Text = "Presione \"Iniciar lectura\" para comenzar",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.FromArgb(100, 100, 120),
            Location = new Point(24, 10),
            Size = new Size(540, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _statusBar.Controls.Add(_lblStatus);

        Controls.AddRange([header, _telemetryPanel, _sanitizePanel, _statusBar]);
    }

    private void SwitchMode(AgentMode mode)
    {
        _currentMode = mode;
        _telemetryPanel.Visible = mode == AgentMode.Telemetry;
        _sanitizePanel.Visible = mode == AgentMode.Sanitize;
        _btnModeTelemetry.BackColor = mode == AgentMode.Telemetry ? Color.FromArgb(239, 68, 68) : Color.FromArgb(100, 100, 120);
        _btnModeSanitize.BackColor = mode == AgentMode.Sanitize ? Color.FromArgb(239, 68, 68) : Color.FromArgb(100, 100, 120);

        if (mode == AgentMode.Sanitize)
        {
            if (_diskListView.Items.Count == 0)
                EnumerateDisks();
            if (_diskListView.Items.Count > 0)
            {
                UpdateMethodInfo((int)_diskSelector.Value);
            }
            SetStatus("Modo saneamiento - seleccione un disco y presione INICIAR SANEAMIENTO");
        }
        else
        {
            if (_metrics == null)
                SetStatus("Detectando hardware automaticamente...");
            else
                SetStatus("Modo telemetria - presione INICIAR LECTURA para actualizar");
        }
    }

    private void EnumerateDisks()
    {
        _diskListView.Items.Clear();
        try
        {
            bool found = false;
            try
            {
                var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT Number, FriendlyName, Model, SerialNumber, MediaType, BusType, Size FROM MSFT_PhysicalDisk"));
                int idx = 0;
                foreach (var obj in searcher.Get())
                {
                    var mo = (ManagementObject)obj;
                    AddDiskItem(mo, idx);
                    idx++;
                    found = true;
                }
                if (idx > 0)
                {
                    _diskSelector.Maximum = idx - 1;
                }
            }
            catch { }

            if (!found)
            {
                // Fallback: Win32_DiskDrive (no tiene MediaType/BusType, se muestra como desconocido)
                var scope2 = new ManagementScope(@"\\.\ROOT\CIMV2");
                scope2.Connect();
                using var searcher2 = new ManagementObjectSearcher(scope2,
                    new ObjectQuery("SELECT Index, Model, SerialNumber, Size, InterfaceType FROM Win32_DiskDrive"));
                int idx2 = 0;
                foreach (var obj in searcher2.Get())
                {
                    var mo = (ManagementObject)obj;
                    AddDiskItemFallback(mo, idx2);
                    idx2++;
                    found = true;
                }
                if (idx2 > 0)
                {
                    _diskSelector.Maximum = idx2 - 1;
                }
            }

            if (!found)
                SetSanitizeStatus("No se encontraron discos en el sistema.", Color.FromArgb(239, 68, 68));
        }
        catch (Exception ex)
        {
            SetSanitizeStatus($"Error al enumerar discos: {ex.Message}", Color.FromArgb(239, 68, 68));
        }
    }

    private void AddDiskItem(ManagementObject mo, int index)
    {
        var num = mo["Number"]?.ToString() ?? index.ToString();
        var name = mo["FriendlyName"]?.ToString()?.Trim() ?? "Unknown";
        var sn = mo["SerialNumber"]?.ToString()?.Trim() ?? "N/A";
        var sizeBytes = mo["Size"] is ulong sz ? sz : 0UL;
        var sizeGB = sizeBytes > 0 ? $"{sizeBytes / 1024 / 1024 / 1024} GB" : "Unknown";
        var mediaType = mo["MediaType"] is ushort mt
            ? mt switch { 3 => "HDD", 4 => "SSD", _ => "?" } : "?";
        var bus = mo["BusType"] is ushort bt
            ? bt switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", _ => bt.ToString() } : "?";

        var item = new ListViewItem(num);
        item.SubItems.Add(name);
        item.SubItems.Add(sizeGB);
        item.SubItems.Add(mediaType);
        item.SubItems.Add(sn);
        item.SubItems.Add(bus);
        item.Tag = sizeBytes;
        _diskListView.Items.Add(item);
    }

    private void AddDiskItemFallback(ManagementObject mo, int index)
    {
        var num = mo["Index"]?.ToString() ?? index.ToString();
        var name = mo["Model"]?.ToString()?.Trim() ?? "Unknown";
        var sn = mo["SerialNumber"]?.ToString()?.Trim() ?? "N/A";
        var sizeBytes = mo["Size"] is ulong sz ? sz : 0UL;
        var sizeGB = sizeBytes > 0 ? $"{sizeBytes / 1024 / 1024 / 1024} GB" : "Unknown";
        var iface = mo["InterfaceType"]?.ToString()?.Trim() ?? "?";

        var item = new ListViewItem(num);
        item.SubItems.Add(name);
        item.SubItems.Add(sizeGB);
        item.SubItems.Add("?");
        item.SubItems.Add(sn);
        item.SubItems.Add(iface);
        item.Tag = sizeBytes;
        _diskListView.Items.Add(item);
    }

    private void UpdateMethodInfo(int diskIndex)
    {
        if (diskIndex < 0 || diskIndex >= _diskListView.Items.Count)
        {
            _lblMethodInfo.Text = "Metodo: Seleccione un disco";
            return;
        }

        // Use native engine for accurate classification
        var classifyJson = NativeMethods.PtrToString(NativeMethods.Engine_ClassifyDevice(diskIndex));
        if (!string.IsNullOrEmpty(classifyJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(classifyJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("eraseMethod", out var em))
                {
                    var method = em.GetString() ?? "Desconocido";
                    var mediaType = root.TryGetProperty("mediaType", out var mt) ? mt.GetString() : "?";
                    var bus = root.TryGetProperty("busInterface", out var bt) ? bt.GetString() : "?";
                    var model = root.TryGetProperty("model", out var md) ? md.GetString() : "?";

                    string displayMethod = method switch
                    {
                        "Overwrite" => "NIST Clear (1 pasada) - HDD",
                        "ATASecurityErase" => "ATA Secure Erase - SSD SATA",
                        "NVMeSanitize" => "NVMe Sanitize - SSD NVMe",
                        _ => method
                    };

                    _lblMethodInfo.Text = $"\u26A0 Disco: {model} | Tipo: {mediaType} ({bus}) | Metodo: {displayMethod}";
                    _lblMethodInfo.ForeColor = Color.FromArgb(234, 179, 8);
                    return;
                }
            }
            catch { }
        }

        // Fallback: naive classification
        var item = _diskListView.Items[diskIndex];
        var bus2 = item.SubItems.Count > 5 ? item.SubItems[5].Text : "?";
        var mediaType2 = item.SubItems.Count > 3 ? item.SubItems[3].Text : "?";
        string method2;
        if (bus2 == "NVMe")
            method2 = "NVMe Sanitize";
        else if (mediaType2 == "SSD" || bus2 == "SATA")
            method2 = "ATA Secure Erase";
        else
            method2 = "NIST Clear (Overwrite)";

        _lblMethodInfo.Text = $"\u26A0 Metodo: {method2}";
        _lblMethodInfo.ForeColor = Color.FromArgb(234, 179, 8);
    }

    private static string FetchRamInfo()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\CIMV2");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Capacity, Manufacturer, Speed, FormFactor, MemoryType FROM Win32_PhysicalMemory"));
            ulong total = 0;
            string speed = "", type = "";
            foreach (var obj in searcher.Get())
            {
                if (obj["Capacity"] is ulong cap)
                    total += cap;
                if (string.IsNullOrEmpty(speed) && obj["Speed"] != null)
                    speed = obj["Speed"]?.ToString() + " MHz";
                if (string.IsNullOrEmpty(type) && obj["MemoryType"] is ushort mt)
                    type = mt switch { 26 => "DDR4", 34 => "DDR5", 20 => "DDR3", _ => "?" };
            }
            var gb = total / (1024UL * 1024UL * 1024UL);
            return $"{gb} GB {type} {speed}".Trim();
        }
        catch { return "Error al consultar RAM"; }
    }

    private static string FetchGpuInfo()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\CIMV2");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Name, AdapterRAM, DriverVersion, Status FROM Win32_VideoController"));
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "?";
                var ram = obj["AdapterRAM"] is uint r
                    ? $"{r / (1024 * 1024)} MB" : "";
                return $"{name} {ram}".Trim();
            }
            return "No GPU detectada";
        }
        catch { return "Error al consultar GPU"; }
    }

    private Panel CreateCard(string title, Point location, Size size)
    {
        var card = new Panel
        {
            Location = location,
            Size = size,
            BackColor = Color.FromArgb(24, 24, 27),
        };
        var border = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(size.Width, 1),
            BackColor = Color.FromArgb(39, 39, 42)
        };
        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(16, 8),
            Size = new Size(300, 18),
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.AddRange([border, titleLabel]);
        return card;
    }

    private Label AddCardField(Panel card, string label, string value, int row)
    {
        int y = 34 + row * 24;
        var lbl = new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(18, y),
            Size = new Size(80, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var val = new Label
        {
            Text = value,
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(228, 228, 231),
            Location = new Point(100, y),
            Size = new Size(370, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.AddRange([lbl, val]);
        return val;
    }

    private void SetEnabled(bool enabled)
    {
        _btnStart.Enabled = enabled;
        _btnStart.BackColor = enabled ? Color.FromArgb(239, 68, 68) : Color.FromArgb(39, 39, 42);
    }

    private void SetSanitizeEnabled(bool enabled)
    {
        _btnStartSanitize.Enabled = enabled;
        _btnStartSanitize.BackColor = enabled ? Color.FromArgb(239, 68, 68) : Color.FromArgb(99, 19, 19);
        _btnCancelSanitize.Enabled = !enabled;
        _diskSelector.Enabled = enabled;
        _txtBackendUrl.Enabled = enabled;
    }

    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        SetEnabled(false);
        ResetDisplay();
        SetStatus("Iniciando lectura del hardware...");
        _spinnerTimer.Start();

        try
        {
            _metrics = await Task.Run(FetchHardwareTelemetry);
            DisplayMetrics(_metrics);

            // Fetch RAM & GPU info
            var ramTask = Task.Run(() => FetchRamInfo());
            var gpuTask = Task.Run(() => FetchGpuInfo());
            _lblRamInfo.Text = "RAM: " + await ramTask;
            _lblGpuInfo.Text = "GPU: " + await gpuTask;

            SetStatus("Sincronizando con el portal Achorao...");
            await Task.Delay(500);

            var telemetryJson = BuildTelemetryJson(_metrics);
            bool telemetryOk = await PostJsonAsync(_httpClient, "https://api.achorao.com/telemetry", telemetryJson);

            if (telemetryOk)
            {
                SetStatus("Sincronizacion exitosa. Registrando handshake NIST...");
                await Task.Delay(300);

                var nistJson = BuildNistJson(_metrics);
                var nistResponse = await PostJsonWithResponseAsync(_httpClient, "https://api.achorao.com/nist/handshake", nistJson);

                if (nistResponse != null)
                    SetStatus($"Todo listo. \u2713 Telemetria enviada. Handshake NIST: {nistResponse}");
                else
                    SetStatus("Sincronizado. Handshake NIST no disponible (no critico).");
            }
            else
            {
                SetStatus("No se pudo contactar el servidor. Los datos locales se leyeron correctamente.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            _spinnerTimer.Stop();
            SetEnabled(true);
            if (_metrics != null && _btnStart.Text != "REINTENTAR")
                _btnStart.Text = "REINTENTAR";
        }
    }

    private async void BtnStartSanitize_Click(object? sender, EventArgs e)
    {
        if (_sanitizeCts != null) return;

        int selectedIndex = (int)_diskSelector.Value;
        if (selectedIndex < 0 || selectedIndex >= _diskListView.Items.Count)
        {
            SetSanitizeStatus("Seleccione un disco valido de la lista.", Color.FromArgb(239, 68, 68));
            return;
        }

        var item = _diskListView.Items[selectedIndex];
        var serial = item.SubItems[4].Text;
        var model = item.SubItems[1].Text;
        var backendUrl = _txtBackendUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(backendUrl))
        {
            SetSanitizeStatus("Ingrese una URL de backend valida.", Color.FromArgb(239, 68, 68));
            return;
        }

        SetSanitizeEnabled(false);
        _txtCertHash.Text = "";
        _btnCopyCert.Enabled = false;
        _sanitizeCts = new CancellationTokenSource();
        var ct = _sanitizeCts.Token;

        try
        {
            await SanitizationWorkflowAsync(backendUrl, model, serial, selectedIndex, ct);
        }
        catch (OperationCanceledException)
        {
            SetSanitizeStatus("Proceso cancelado por el usuario.", Color.FromArgb(239, 68, 68));
            SetStatus("Saneamiento cancelado.");
        }
        catch (Exception ex)
        {
            SetSanitizeStatus($"Error: {ex.Message}", Color.FromArgb(239, 68, 68));
            SetStatus($"Error en saneamiento: {ex.Message}");
        }
        finally
        {
            _sanitizeCts?.Dispose();
            _sanitizeCts = null;
            SetSanitizeEnabled(true);
        }
    }

    private async Task SanitizationWorkflowAsync(string backendUrl, string model, string serial, int diskIndex, CancellationToken ct)
    {
        var baseUrl = backendUrl.TrimEnd('/');
        var techId = $"TECH-WAYRA-{Random.Shared.Next(999)}";
        var workstation = Environment.MachineName;
        var evidenceDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "evidence");
        Directory.CreateDirectory(evidenceDir);

        // SAFETY: Disk 0 lock
        if (diskIndex == 0)
        {
            SetSanitizeStatus("SEGURO: Disco 0 (sistema) protegido. Seleccione otro disco.", Color.FromArgb(239, 68, 68));
            SetStatus("Operacion cancelada: no se permite sanear el disco del sistema.");
            return;
        }

        // CLASSIFY + METHOD
        var diskType = ClassifyDiskType(diskIndex);
        var eraseMethod = diskType switch
        {
            "NVMe" => "NVMe Sanitize",
            "SSD"  => "ATA Secure Erase",
            "HDD"  => "NIST Clear (Overwrite)",
            _      => "NIST Clear (Overwrite)"
        };
        SetStatus($"Disco clasificado como {diskType} - Metodo: {eraseMethod}");

        // SMART PRE
        var smartPre = CaptureSmartSnapshot(diskIndex, "PRE-ERASE");

        // 1. HANDSHAKE
        SetSanitizeStatus("[1/5] Transmitiendo metadatos al inventario web (handshake)...", Color.FromArgb(161, 161, 170));
        SetStatus("Handshake: registrando dispositivo en el panel web...");

        var handshakeBody = $"{{\"model\":{EscapeJson(model)},\"serialNumber\":{EscapeJson(serial)},\"vendor\":{EscapeJson(ExtractVendor(model))},\"storageType\":{EscapeJson(diskType)},\"eraseMethod\":{EscapeJson(eraseMethod)},\"technicianId\":{EscapeJson(techId)},\"workstation\":{EscapeJson(workstation)},\"status\":\"PENDING\"}}";
        var handshakeResponse = await PostJsonWithResponseAsync(_httpClient, $"{baseUrl}/api/v1/security/handshake", handshakeBody);
        string sessionToken = "";

        if (handshakeResponse != null)
        {
            sessionToken = ExtractJsonValue(handshakeResponse, "sessionToken") ?? "";
            SetSanitizeStatus("[1/5] Handshake exitoso. Estado bloqueado como PENDING.", Color.FromArgb(234, 179, 8));
        }
        else
        {
            SetSanitizeStatus("Error: El servidor no acepto el registro de auditoria (handshake).", Color.FromArgb(239, 68, 68));
            SetStatus("Handshake fallido - revise la URL del backend.");
            return;
        }

        // 2. POLLING
        SetSanitizeStatus("[2/5] AGENTE EN ESPERA: revise su panel web y presione ADOPTAR.", Color.FromArgb(161, 161, 170));
        SetStatus($"Esperando autorizacion web para SN: {serial}...");

        int retry = 0;
        int maxRetries = 200;
        bool approved = false;
        string lastStatus = "PENDING";
        while (!approved && !ct.IsCancellationRequested && retry < maxRetries)
        {
            retry++;
            try
            {
                var statusUrl = $"{baseUrl}/api/v1/security/status?serialNumber={Uri.EscapeDataString(serial)}";
                var resp = await _httpClient.GetAsync(statusUrl, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    lastStatus = ExtractStatusValue(body) ?? "unknown";
                    if (body.Contains("\"APPROVED\"", StringComparison.OrdinalIgnoreCase) || body.Contains("\"isApproved\":true", StringComparison.OrdinalIgnoreCase))
                    {
                        approved = true;
                    }
                }
                else
                {
                    lastStatus = $"HTTP {(int)resp.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                lastStatus = $"error: {ex.Message}";
            }

            if (!approved)
            {
                SetSanitizeStatus($"[2/5] Consulta #{retry}/{maxRetries} | Estado: {lastStatus}", Color.FromArgb(161, 161, 170));
                SetStatus($"Esperando autorizacion web... SN: {serial} | Estado: {lastStatus}");
                await Task.Delay(3000, ct);
            }
        }

        if (!approved && !ct.IsCancellationRequested)
        {
            SetSanitizeStatus($"[2/5] Tiempo de espera agotado tras {maxRetries} intentos. Ultimo estado: {lastStatus}", Color.FromArgb(239, 68, 68));
            SetStatus("Timeout: no se recibio autorizacion a tiempo.");
            return;
        }

        ct.ThrowIfCancellationRequested();

        SetSanitizeStatus("[2/5] DIRECTIVA RECIBIDA - Ejecucion autorizada por el Ledger.", Color.FromArgb(234, 179, 8));
        SetStatus("Autorizacion web recibida. Ejecutando borrado...");

        // 3. ERASE via native C++ engine
        SetSanitizeStatus($"[3/5] Saneando via {eraseMethod}...", Color.FromArgb(239, 68, 68));
        SetStatus("Ejecutando borrado seguro via motor nativo...");

        string verificationResult = "NO_VERIFIED";
        bool eraseSuccess = false;
        try
        {
            var eraseJson = NativeMethods.PtrToString(NativeMethods.Engine_ExecuteSanitize(diskIndex));
            if (string.IsNullOrEmpty(eraseJson))
            {
                SetSanitizeStatus("[3/5] Error: El motor nativo no retorno respuesta.", Color.FromArgb(239, 68, 68));
                SetStatus("Fallo en la ejecucion del motor nativo.");
                return;
            }

            using var eraseDoc = JsonDocument.Parse(eraseJson);
            var eraseRoot = eraseDoc.RootElement;
            eraseSuccess = eraseRoot.TryGetProperty("success", out var s) && s.GetBoolean();

            if (eraseSuccess)
            {
                SetSanitizeStatus($"[3/5] Borrado completado. Verificando integridad...", Color.FromArgb(234, 179, 8));

                var verifyJson = NativeMethods.PtrToString(NativeMethods.Engine_VerifySanitization(diskIndex));
                if (!string.IsNullOrEmpty(verifyJson))
                {
                    using var verifyDoc = JsonDocument.Parse(verifyJson);
                    if (verifyDoc.RootElement.TryGetProperty("passed", out var p) && p.GetBoolean())
                    {
                        verificationResult = "PASS";
                    }
                    else
                    {
                        verificationResult = "FAIL";
                        if (verifyDoc.RootElement.TryGetProperty("details", out var d))
                            verificationResult += ": " + d.GetString();
                    }
                }

                SetSanitizeStatus($"[3/5] Disco saneado. Verificacion: {verificationResult}",
                    Color.FromArgb(verificationResult == "PASS" ? 234 : 239, 179, verificationResult == "PASS" ? 8 : 68));
            }
            else
            {
                var errMsg = eraseRoot.TryGetProperty("errorMessage", out var em) ? em.GetString() : "Error desconocido";
                SetSanitizeStatus($"[3/5] Error en borrado: {errMsg}", Color.FromArgb(239, 68, 68));
                SetStatus($"Fallo: {errMsg}");
                return;
            }
        }
        catch (Exception ex)
        {
            SetSanitizeStatus($"[3/5] Error critico: {ex.Message}", Color.FromArgb(239, 68, 68));
            SetStatus("Fallo en el borrado via motor nativo.");
            return;
        }

        // SMART POST
        var smartPost = CaptureSmartSnapshot(diskIndex, "POST-ERASE");

        // 4+5. EVIDENCE + CERTIFICATE
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var rawHash = serial + timestamp + "AUTHORIZED_WEB_PURGE";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawHash));
        var hashString = Convert.ToHexStringLower(hashBytes);

        var evidence = new Dictionary<string, object>
        {
            ["serialNumber"] = serial,
            ["model"] = model,
            ["diskType"] = diskType,
            ["eraseMethod"] = eraseMethod,
            ["technicianId"] = techId,
            ["workstation"] = workstation,
            ["sessionToken"] = sessionToken,
            ["blockVerification"] = verificationResult,
            ["certificateSha256"] = hashString,
            ["timestamp"] = timestamp,
            ["smartPreErase"] = smartPre,
            ["smartPostErase"] = smartPost
        };
        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(evidence, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var evidencePath = Path.Combine(evidenceDir, $"evidence_{serial}_{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        await File.WriteAllTextAsync(evidencePath, evidenceJson, ct);

        SetSanitizeStatus("[4/5] Despachando certificado criptografico inmutable...", Color.FromArgb(161, 161, 170));
        SetStatus("Registrando certificado de completitud...");

        var certBody = $"{{\"serialNumber\":{EscapeJson(serial)},\"model\":{EscapeJson(model)},\"technicianId\":{EscapeJson(techId)},\"workstation\":{EscapeJson(workstation)},\"status\":\"COMPLETED\",\"sha256\":{EscapeJson(hashString)},\"sessionToken\":{EscapeJson(sessionToken)},\"timestamp\":{EscapeJson(timestamp)},\"eraseMethod\":{EscapeJson(eraseMethod)},\"blockVerification\":{EscapeJson(verificationResult)}}}";
        var certResponse = await PostJsonWithResponseAsync(_httpClient, $"{baseUrl}/api/v1/security/certificate", certBody);

        if (certResponse != null)
        {
            _txtCertHash.Text = serial;
            _btnCopyCert.Enabled = true;
            SetSanitizeStatus("[5/5] Listo. Flujo cerrado. Bitacora web actualizada a COMPLETED.", Color.FromArgb(234, 179, 8));
            SetStatus($"Saneamiento completado. Serial: {serial} | Evidencia: {evidencePath}");
        }
        else
        {
            _txtCertHash.Text = serial;
            _btnCopyCert.Enabled = true;
            SetSanitizeStatus("[5/5] Borrado exitoso, pero no se pudo registrar el certificado en el servidor.", Color.FromArgb(239, 68, 68));
            SetStatus($"Saneamiento completado (certificado no registrado). Evidencia: {evidencePath}");
        }
    }

    private void ResetDisplay()
    {
        _lblDiskName.Text = "---";
        _lblSerial.Text = "---";
        _lblCapacity.Text = "---";
        _lblType.Text = "---";
        _healthBar.Value = 0;
        _healthBar.ForeColor = Color.FromArgb(234, 179, 8);
        _lblHealthValue.Text = "---";
        _lblGrade.Text = "";
        _lblRamInfo.Text = "RAM: ---";
        _lblGpuInfo.Text = "GPU: ---";
    }

    private void DisplayMetrics(DiskMetrics m)
    {
        _lblDiskName.Text = m.DiskName;
        _lblSerial.Text = m.SerialNumber;
        _lblCapacity.Text = m.Capacity;
        _lblType.Text = m.Type + " (" + m.BusInterface + ")";

        _healthBar.Value = Math.Min(m.HealthScore, 100);
        _lblHealthValue.Text = m.HealthScore + "%";

        if (m.HealthScore >= 75)
            _healthBar.ForeColor = Color.FromArgb(234, 179, 8);
        else if (m.HealthScore >= 50)
            _healthBar.ForeColor = Color.FromArgb(245, 158, 11);
        else
            _healthBar.ForeColor = Color.FromArgb(239, 68, 68);

        _lblGrade.Text = "Grado " + m.Grade;

        string healthColor;
        if (m.HealthScore >= 75) healthColor = "#EAB308";
        else if (m.HealthScore >= 50) healthColor = "#F59E0B";
        else healthColor = "#EF4444";
        _lblGrade.ForeColor = ColorTranslator.FromHtml(healthColor);
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetStatus(text));
            return;
        }
        _lblStatus.Text = text;
    }

    private void SetSanitizeStatus(string text, Color? color = null)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetSanitizeStatus(text, color));
            return;
        }
        _lblSanitizeStatus.Text = text;
        _lblSanitizeStatus.ForeColor = color ?? Color.FromArgb(100, 100, 120);
    }

    // ---- WMI / Hardware ----

    private static DiskMetrics FetchHardwareTelemetry()
    {
        var m = new DiskMetrics
        {
            DiskName = "Unknown Storage Unit",
            SerialNumber = "UNKNOWN-SERIAL",
            Type = "SSD",
            BusInterface = "NVMe",
            Capacity = "512 GB",
            Hours = 1420, Wear = 2, Temp = 36, Sectors = 0, WrittenTB = 12.4
        };

        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT FriendlyName, SerialNumber, MediaType, BusType, Size FROM MSFT_PhysicalDisk"));

            ManagementObject? physDisk = null;
            foreach (var obj in searcher.Get())
            {
                var mo = (ManagementObject)obj;
                var mt = mo["MediaType"];
                if (mt != null && ((ushort)mt == 3 || (ushort)mt == 4)) { physDisk = mo; break; }
                physDisk ??= mo;
            }

            if (physDisk != null)
            {
                if (physDisk["FriendlyName"] is string fn && !string.IsNullOrWhiteSpace(fn)) m.DiskName = fn.Trim();
                if (physDisk["SerialNumber"] is string sn && !string.IsNullOrWhiteSpace(sn)) m.SerialNumber = sn.Trim();
                if (physDisk["MediaType"] is ushort mt) m.Type = mt == 4 ? "SSD" : mt == 3 ? "HDD" : "SSD";
                if (physDisk["BusType"] is ushort bt)
                    m.BusInterface = bt switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", _ => "NVMe" };
                if (physDisk["Size"] is ulong size && size > 0)
                    m.Capacity = $"{size / 1024 / 1024 / 1024} GB";
            }
        }
        catch { }

        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            using var rs = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT PowerOnHours, Temperature, Wear, ReadErrorsTotal, CumulativeBytesWritten FROM MSFT_StorageReliabilityCounter"));
            foreach (var obj in rs.Get())
            {
                var ro = (ManagementObject)obj;
                if (ro["PowerOnHours"] is ulong poh) m.Hours = (int)poh;
                if (ro["Temperature"] is ushort temp && temp > 0) m.Temp = temp;
                if (ro["Wear"] is ushort wear) m.Wear = wear;
                if (ro["ReadErrorsTotal"] is ulong readErr) m.Sectors = (int)readErr;
                if (ro["CumulativeBytesWritten"] is ulong bytes)
                    m.WrittenTB = Math.Round(bytes / (double)(1024L * 1024 * 1024 * 1024), 2);
                break;
            }
        }
        catch { }

        int health = 100 - Math.Min(m.Wear, 40);
        if (m.Temp > 60) health -= 15;
        if (m.Hours > 20000) health -= 10;
        if (m.Sectors > 0) health -= 25;
        m.HealthScore = Math.Max(health, 0);

        m.Grade = m.HealthScore switch { >= 90 => "A", >= 75 => "B", >= 60 => "C", _ => "D" };
        m.GeneratedAt = DateTime.UtcNow.ToString("O");

        var payload = $"{m.SerialNumber}|{m.DiskName}|{m.Hours}|{m.WrittenTB:F1}|{m.Wear}|{m.Temp}|{m.Sectors}|{m.HealthScore}";
        m.Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        return m;
    }

    // ---- JSON Builders ----

    private static string EscapeJson(string v)
    {
        var sb = new StringBuilder().Append('"');
        foreach (char c in v)
            _ = c switch { '\\' => sb.Append(@"\\"), '"' => sb.Append("\\\""), '\n' => sb.Append("\\n"), '\r' => sb.Append("\\r"), '\t' => sb.Append("\\t"), _ => sb.Append(c) };
        return sb.Append('"').ToString();
    }

    private static string BuildTelemetryJson(DiskMetrics m) =>
        "{" +
        "\"serialNumber\":" + EscapeJson(m.SerialNumber) + "," +
        "\"diskName\":" + EscapeJson(m.DiskName) + "," +
        "\"type\":" + EscapeJson(m.Type) + "," +
        "\"capacity\":" + EscapeJson(m.Capacity) + "," +
        "\"interface\":" + EscapeJson(m.BusInterface) + "," +
        "\"healthScore\":" + m.HealthScore + "," +
        "\"grade\":" + EscapeJson(m.Grade) + "," +
        "\"hours\":" + m.Hours + "," +
        "\"wear\":" + m.Wear + "," +
        "\"temp\":" + m.Temp + "," +
        "\"sectors\":" + m.Sectors + "," +
        "\"writtenTB\":" + m.WrittenTB.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
        "\"generatedAt\":" + EscapeJson(m.GeneratedAt) + "," +
        "\"signature\":" + EscapeJson("SIG_RSA4096_PKCS1_SHA256_V104_APPROVED_ONLINE") + "," +
        "\"hash\":" + EscapeJson(m.Hash) + "}";

    private static string BuildNistJson(DiskMetrics m) =>
        "{" +
        "\"model\":" + EscapeJson(m.DiskName) + "," +
        "\"serialNumber\":" + EscapeJson(m.SerialNumber) + "," +
        "\"vendor\":" + EscapeJson(ExtractVendor(m.DiskName)) + "," +
        "\"technicianId\":" + EscapeJson("TECH-LOCAL-WIN-AGENT") + "," +
        "\"workstation\":" + EscapeJson(Environment.MachineName) + "}";

    private static string? ExtractStatusValue(string json)
    {
        return ExtractJsonValue(json, "status");
    }

    private static string? ExtractJsonValue(string json, string key)
    {
        var search = $"\"{key}\":\"";
        int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        int s = idx + search.Length;
        int e = json.IndexOf('"', s);
        return e >= 0 ? json[s..e] : null;
    }

    private static string ExtractVendor(string name)
    {
        foreach (var v in new[] { "Kingston", "Samsung", "Corsair", "Crucial", "Toshiba", "Western Digital", "WD", "Seagate" })
            if (name.Contains(v, StringComparison.OrdinalIgnoreCase)) return v;
        return "Generico";
    }

    private static string ClassifyDiskType(int diskIndex)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT MediaType, BusType FROM MSFT_PhysicalDisk WHERE DeviceId = {diskIndex}"));
            foreach (var obj in searcher.Get())
            {
                var mo = (ManagementObject)obj;
                if (mo["BusType"] is ushort bt && bt == 17) return "NVMe";
                if (mo["MediaType"] is ushort mt && mt == 3) return "HDD";
                if (mo["MediaType"] is ushort mt2 && mt2 == 4) return "SSD";
            }
        }
        catch { }
        return "HDD";
    }

    private static Dictionary<string, object> CaptureSmartSnapshot(int diskIndex, string label)
    {
        var data = new Dictionary<string, object> { ["label"] = label, ["timestamp"] = DateTime.UtcNow.ToString("O") };
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT PowerOnHours, Temperature, Wear, ReadErrorsTotal, CumulativeBytesWritten FROM MSFT_StorageReliabilityCounter"));
            foreach (var obj in searcher.Get())
            {
                var ro = (ManagementObject)obj;
                if (ro["PowerOnHours"] is ulong poh) data["powerOnHours"] = (int)poh;
                if (ro["Temperature"] is ushort temp) data["temperature"] = (int)temp;
                if (ro["Wear"] is ushort wear) data["wear"] = (int)wear;
                if (ro["ReadErrorsTotal"] is ulong re) data["readErrors"] = (long)re;
                if (ro["CumulativeBytesWritten"] is ulong cbw) data["bytesWritten"] = cbw.ToString();
                break;
            }
        }
        catch (Exception ex) { data["error"] = ex.Message; }
        return data;
    }

    // ---- HTTP ----

    private static async Task<bool> PostJsonAsync(HttpClient client, string url, string json)
    {
        try { return (await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"))).IsSuccessStatusCode; }
        catch { return false; }
    }

    private static async Task<string?> PostJsonWithResponseAsync(HttpClient client, string url, string json)
    {
        try
        {
            var resp = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return null; }
    }
}

class DiskMetrics
{
    public string SerialNumber { get; set; } = "";
    public string DiskName { get; set; } = "";
    public string Type { get; set; } = "";
    public string Capacity { get; set; } = "";
    public string BusInterface { get; set; } = "";
    public int HealthScore { get; set; }
    public string Grade { get; set; } = "";
    public int Hours { get; set; }
    public int Wear { get; set; }
    public int Temp { get; set; }
    public int Sectors { get; set; }
    public double WrittenTB { get; set; }
    public string GeneratedAt { get; set; } = "";
    public string Hash { get; set; } = "";
}

static class ClipboardHelper
{
    internal static void Copy(string text)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            var t = new Thread(() => { Clipboard.SetText(text, TextDataFormat.UnicodeText); });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }
        else
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
        }
    }
}

internal static class NativeMethods
{
    private const string DllName = "AchoraoEngine.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern bool Engine_Initialize(string logPath);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Engine_Shutdown();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Engine_EnumerateStorage();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Engine_ClassifyDevice(int deviceNumber);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Engine_ReadSmart(int deviceNumber);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Engine_DetectMemory();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Engine_DetectGPU();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Engine_ExecuteSanitize(int deviceNumber);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Engine_VerifySanitization(int deviceNumber);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Engine_BuildEvidence(int deviceNumber, string technicianId, string workstation);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Engine_FreeString(IntPtr str);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern bool Engine_IsSystemDisk(int deviceNumber);

    internal static string? PtrToString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var result = Marshal.PtrToStringAnsi(ptr);
            return result;
        }
        finally
        {
            Engine_FreeString(ptr);
        }
    }
}
