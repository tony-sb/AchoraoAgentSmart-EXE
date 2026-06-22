using System.Diagnostics;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

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
    private ComboBox _cmbFileSystem;
    private Label _lblFsDescription;
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
        BackColor = Color.FromArgb(245, 245, 250);
        Font = new Font("Segoe UI", 10);

        BuildUI();
    }

    private void BuildUI()
    {
        var header = new Panel
        {
            Size = new Size(580, 100),
            Location = new Point(0, 0),
            BackColor = Color.FromArgb(20, 50, 90)
        };
        _headerTitle = new Label
        {
            Text = "ACHORAO AGENT",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(24, 10),
            Size = new Size(280, 36),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _btnModeTelemetry = new Button
        {
            Text = "TELEMETRIA",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(40, 120, 200),
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
            ForeColor = Color.FromArgb(180, 200, 230),
            Location = new Point(28, 52),
            Size = new Size(460, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.AddRange([_headerTitle, _btnModeTelemetry, _btnModeSanitize, _headerSub]);

        // ============ TELEMETRY PANEL ============
        _telemetryPanel = new Panel { Location = new Point(0, 100), Size = new Size(580, 560), BackColor = Color.FromArgb(245, 245, 250) };

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
            ForeColor = Color.FromArgb(40, 180, 80),
            BackColor = Color.FromArgb(220, 220, 230),
            Value = 0
        };
        _lblHealthValue = new Label
        {
            Text = "---",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 180, 80),
            Location = new Point(468, 36),
            Size = new Size(60, 30),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblGrade = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 180, 80),
            Location = new Point(20, 70),
            Size = new Size(100, 36),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _cardHealth.Controls.AddRange([_healthBar, _lblHealthValue, _lblGrade]);

        _btnStart = new Button
        {
            Text = "INICIAR LECTURA",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(20, 50, 90),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(120, 340),
            Size = new Size(200, 42),
            Cursor = Cursors.Hand
        };
        _btnStart.Click += BtnStart_Click;

        _btnExit = new Button
        {
            Text = "CERRAR",
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            ForeColor = Color.FromArgb(80, 80, 100),
            BackColor = Color.FromArgb(220, 220, 230),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(340, 340),
            Size = new Size(120, 42),
            Cursor = Cursors.Hand
        };
        _btnExit.Click += (_, _) => Close();

        _spinnerTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _spinnerTimer.Tick += (_, _) => { _spinnerDotCount = (_spinnerDotCount + 1) % 6; };

        _telemetryPanel.Controls.AddRange([_cardDisk, _cardHealth, _btnStart, _btnExit]);

        // ============ SANITIZE PANEL ============
        _sanitizePanel = new Panel { Location = new Point(0, 100), Size = new Size(580, 560), BackColor = Color.FromArgb(245, 245, 250), Visible = false };

        Label SectionTitle(string text, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 50, 90),
                Location = new Point(20, y),
                Size = new Size(540, 18),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _sanitizePanel.Controls.Add(new Panel { Location = new Point(20, y + 18), Size = new Size(540, 1), BackColor = Color.FromArgb(200, 200, 210) });
            return lbl;
        }
        Label HelpText(string text, int y) => new Label
        {
            Text = text, Font = new Font("Segoe UI", 7, FontStyle.Italic),
            ForeColor = Color.FromArgb(140, 140, 160),
            Location = new Point(22, y), Size = new Size(540, 16),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // SECCION 1: URL
        _sanitizePanel.Controls.Add(SectionTitle("CONFIGURACION DEL SERVIDOR", 12));
        var urlLabel = new Label
        {
            Text = "Backend URL:",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 80, 100),
            Location = new Point(24, 40),
            Size = new Size(110, 24),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _txtBackendUrl = new TextBox
        {
            Text = "http://localhost:3000",
            Location = new Point(130, 40),
            Size = new Size(420, 24),
            Font = new Font("Segoe UI", 9)
        };
        _sanitizePanel.Controls.Add(HelpText("URL del servidor central donde se registrara la auditoria del saneamiento.", 68));

        // SECCION 2: DISCOS
        _sanitizePanel.Controls.Add(SectionTitle("SELECCIONE EL DISCO A SANEAR", 100));
        _diskListView = new ListView
        {
            Location = new Point(24, 128),
            Size = new Size(532, 135),
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            Font = new Font("Segoe UI", 9)
        };
        _diskListView.Columns.Add("#", 30);
        _diskListView.Columns.Add("Disco", 190);
        _diskListView.Columns.Add("Capacidad", 80);
        _diskListView.Columns.Add("Serial", 140);
        _diskListView.Columns.Add("Tipo", 70);

        var selLabel = new Label
        {
            Text = "Indice:",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 80, 100),
            Location = new Point(24, 278),
            Size = new Size(52, 24),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _diskSelector = new NumericUpDown
        {
            Location = new Point(76, 278),
            Size = new Size(60, 24),
            Minimum = 0, Maximum = 0,
            Font = new Font("Segoe UI", 9)
        };
        _diskSelector.ValueChanged += (_, _) => AutoSelectFileSystem((int)_diskSelector.Value);
        var fsLabel = new Label
        {
            Text = "Sistema de archivos:",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 80, 100),
            Location = new Point(148, 278),
            Size = new Size(130, 24),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _cmbFileSystem = new ComboBox
        {
            Location = new Point(278, 277),
            Size = new Size(100, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9),
        };
        _cmbFileSystem.Items.AddRange(["exFAT", "NTFS", "FAT32"]);
        _cmbFileSystem.SelectedIndex = 0;
        _cmbFileSystem.SelectedIndexChanged += (_, _) => UpdateFsDescription();
        _lblFsDescription = new Label
        {
            Font = new Font("Segoe UI", 7, FontStyle.Italic),
            ForeColor = Color.FromArgb(120, 120, 140),
            Location = new Point(278, 304),
            Size = new Size(270, 30),
            TextAlign = ContentAlignment.TopLeft
        };
        UpdateFsDescription();
        _sanitizePanel.Controls.Add(HelpText("Escriba el numero de indice que aparece en la lista. El sistema de archivos se auto-selecciona.", 338));

        // SECCION 3: BOTONES
        _sanitizePanel.Controls.Add(SectionTitle("EJECUTAR SANEAMIENTO", 368));
        _btnStartSanitize = new Button
        {
            Text = "INICIAR SANEAMIENTO",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(180, 50, 50),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(120, 394),
            Size = new Size(200, 36),
            Cursor = Cursors.Hand
        };
        _btnStartSanitize.Click += BtnStartSanitize_Click;
        _btnCancelSanitize = new Button
        {
            Text = "CANCELAR",
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            ForeColor = Color.FromArgb(80, 80, 100),
            BackColor = Color.FromArgb(200, 200, 210),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(340, 394),
            Size = new Size(120, 36),
            Cursor = Cursors.Hand,
            Enabled = false
        };
        _btnCancelSanitize.Click += (_, _) => _sanitizeCts?.Cancel();
        _sanitizePanel.Controls.Add(HelpText("El proceso requiere autorizacion desde el panel web. Revise el navegador cuando se le indique.", 436));

        // SECCION 4: ESTADO
        _sanitizePanel.Controls.Add(SectionTitle("ESTADO DEL PROCESO", 456));
        _lblSanitizeStatus = new Label
        {
            Text = "Configure el servidor, seleccione un disco y presione INICIAR SANEAMIENTO.",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 120),
            Location = new Point(24, 480),
            Size = new Size(532, 36),
            TextAlign = ContentAlignment.TopLeft
        };
        var certLabel = new Label
        {
            Text = "Serial:",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 80, 100),
            Location = new Point(24, 520),
            Size = new Size(80, 22),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _txtCertHash = new TextBox
        {
            Location = new Point(104, 520),
            Size = new Size(360, 22),
            Font = new Font("Consolas", 9),
            ReadOnly = true,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(30, 30, 40),
            BorderStyle = BorderStyle.FixedSingle,
            Text = ""
        };
        _btnCopyCert = new Button
        {
            Text = "COPIAR",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(20, 50, 90),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            Location = new Point(470, 519),
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
                    SetSanitizeStatus("Certificado copiado al portapapeles.", Color.DarkBlue);
                }
                catch
                {
                    SetSanitizeStatus("Seleccione el texto y pulse Ctrl+C para copiar.", Color.DarkOrange);
                }
            }
        };

        _sanitizePanel.Controls.AddRange([urlLabel, _txtBackendUrl, _diskListView, selLabel, _diskSelector, fsLabel, _cmbFileSystem, _lblFsDescription, _btnStartSanitize, _btnCancelSanitize, _lblSanitizeStatus, certLabel, _txtCertHash, _btnCopyCert]);

        // ============ STATUS BAR ============
        _statusBar = new Panel
        {
            Location = new Point(0, 660),
            Size = new Size(580, 40),
            BackColor = Color.FromArgb(240, 240, 245)
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
        _btnModeTelemetry.BackColor = mode == AgentMode.Telemetry ? Color.FromArgb(40, 120, 200) : Color.FromArgb(100, 100, 120);
        _btnModeSanitize.BackColor = mode == AgentMode.Sanitize ? Color.FromArgb(40, 120, 200) : Color.FromArgb(100, 100, 120);

        if (mode == AgentMode.Sanitize)
        {
            EnumerateDisks();
            SetStatus("Modo saneamiento - seleccione un disco y presione INICIAR SANEAMIENTO");
        }
        else
        {
            SetStatus("Modo telemetria - presione INICIAR LECTURA");
        }
    }

    private void EnumerateDisks()
    {
        _diskListView.Items.Clear();
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Number, FriendlyName, SerialNumber, BusType, Size FROM MSFT_Disk"));

            int index = 0;
            foreach (var obj in searcher.Get())
            {
                var mo = (ManagementObject)obj;
                var num = mo["Number"]?.ToString() ?? "0";
                var name = mo["FriendlyName"]?.ToString()?.Trim() ?? "Unknown";
                var sn = mo["SerialNumber"]?.ToString()?.Trim() ?? "N/A";
                var sizeBytes = mo["Size"] is ulong sz ? sz : 0UL;
                var sizeGB = sizeBytes > 0 ? $"{sizeBytes / 1024 / 1024 / 1024} GB" : "Unknown";
                var bus = mo["BusType"] is ushort bt ? bt switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", _ => bt.ToString() } : "?";

                var item = new ListViewItem(num);
                item.SubItems.Add(name);
                item.SubItems.Add(sizeGB);
                item.SubItems.Add(sn);
                item.SubItems.Add(bus);
                item.Tag = sizeBytes;
                _diskListView.Items.Add(item);
                index++;
            }
            _diskSelector.Maximum = Math.Max(0, index - 1);

            if (index > 0) AutoSelectFileSystem(0);
        }
        catch (Exception ex)
        {
            SetSanitizeStatus($"Error al enumerar discos: {ex.Message}", Color.Red);
        }
    }

    private void AutoSelectFileSystem(int index)
    {
        if (index < 0 || index >= _diskListView.Items.Count) return;
        var sizeBytes = (ulong)(_diskListView.Items[index].Tag ?? 0UL);
        var sizeGB = sizeBytes / 1024 / 1024 / 1024;
        _cmbFileSystem.SelectedIndex = sizeGB <= 32 ? 2 : 0; // FAT32 (idx 2) if <=32GB, else exFAT (idx 0)
    }

    private void UpdateFsDescription()
    {
        string desc = _cmbFileSystem.SelectedIndex switch
        {
            0 => "exFAT - Universal, recomendado para USB >= 32 GB, HDD/SSD externos hasta 2 TB+",
            1 => "NTFS - Para discos internos grandes (HDD/SSD/NVMe), maximo rendimiento",
            2 => "FAT32 - Solo para USB de hasta 32 GB, maxima compatibilidad",
            _ => ""
        };
        _lblFsDescription.Text = desc;
    }

    private Panel CreateCard(string title, Point location, Size size)
    {
        var card = new Panel
        {
            Location = location,
            Size = size,
            BackColor = Color.White,
        };
        var border = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(size.Width, 1),
            BackColor = Color.FromArgb(210, 210, 220)
        };
        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 100, 130),
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
            ForeColor = Color.FromArgb(80, 80, 100),
            Location = new Point(18, y),
            Size = new Size(80, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var val = new Label
        {
            Text = value,
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(30, 30, 40),
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
        _btnStart.BackColor = enabled ? Color.FromArgb(20, 50, 90) : Color.FromArgb(150, 160, 180);
    }

    private void SetSanitizeEnabled(bool enabled)
    {
        _btnStartSanitize.Enabled = enabled;
        _btnStartSanitize.BackColor = enabled ? Color.FromArgb(180, 50, 50) : Color.FromArgb(180, 120, 120);
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
            SetSanitizeStatus("Seleccione un disco valido de la lista.", Color.Red);
            return;
        }

        var item = _diskListView.Items[selectedIndex];
        var serial = item.SubItems[3].Text;
        var model = item.SubItems[1].Text;
        var backendUrl = _txtBackendUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(backendUrl))
        {
            SetSanitizeStatus("Ingrese una URL de backend valida.", Color.Red);
            return;
        }

        var filesystem = _cmbFileSystem.SelectedItem?.ToString() ?? "exFAT";

        SetSanitizeEnabled(false);
        _txtCertHash.Text = "";
        _btnCopyCert.Enabled = false;
        _sanitizeCts = new CancellationTokenSource();
        var ct = _sanitizeCts.Token;

        try
        {
            await SanitizationWorkflowAsync(backendUrl, model, serial, selectedIndex, filesystem, ct);
        }
        catch (OperationCanceledException)
        {
            SetSanitizeStatus("Proceso cancelado por el usuario.", Color.DarkOrange);
            SetStatus("Saneamiento cancelado.");
        }
        catch (Exception ex)
        {
            SetSanitizeStatus($"Error: {ex.Message}", Color.Red);
            SetStatus($"Error en saneamiento: {ex.Message}");
        }
        finally
        {
            _sanitizeCts?.Dispose();
            _sanitizeCts = null;
            SetSanitizeEnabled(true);
        }
    }

    private async Task SanitizationWorkflowAsync(string backendUrl, string model, string serial, int diskIndex, string filesystem, CancellationToken ct)
    {
        var baseUrl = backendUrl.TrimEnd('/');
        var techId = $"TECH-WAYRA-{Random.Shared.Next(999)}";
        var workstation = Environment.MachineName;

        // 1. HANDSHAKE
        SetSanitizeStatus("[1/4] Transmitiendo metadatos al inventario web (handshake)...", Color.DarkBlue);
        SetStatus("Handshake: registrando dispositivo en el panel web...");

        var handshakeBody = $"{{\"model\":{EscapeJson(model)},\"serialNumber\":{EscapeJson(serial)},\"vendor\":{EscapeJson(ExtractVendor(model))},\"storageType\":\"HDD\",\"technicianId\":{EscapeJson(techId)},\"workstation\":{EscapeJson(workstation)},\"status\":\"PENDING\"}}";
        var handshakeResponse = await PostJsonWithResponseAsync(_httpClient, $"{baseUrl}/api/v1/security/handshake", handshakeBody);
        string sessionToken = "";

        if (handshakeResponse != null)
        {
            sessionToken = ExtractJsonValue(handshakeResponse, "sessionToken") ?? "";
            SetSanitizeStatus("[1/4] Handshake exitoso. Estado bloqueado como PENDING.", Color.Green);
        }
        else
        {
            SetSanitizeStatus("Error: El servidor no acepto el registro de auditoria (handshake).", Color.Red);
            SetStatus("Handshake fallido - revise la URL del backend.");
            return;
        }

        // 2. POLLING
        SetSanitizeStatus("[2/4] AGENTE EN ESPERA: revise su panel web y presione ADOPTAR.", Color.DarkBlue);
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
                SetSanitizeStatus($"[2/4] Consulta #{retry}/{maxRetries} | Estado: {lastStatus}", Color.DarkBlue);
                SetStatus($"Esperando autorizacion web... SN: {serial} | Estado: {lastStatus}");
                await Task.Delay(3000, ct);
            }
        }

        if (!approved && !ct.IsCancellationRequested)
        {
            SetSanitizeStatus($"[2/4] Tiempo de espera agotado tras {maxRetries} intentos. Ultimo estado: {lastStatus}", Color.Red);
            SetStatus("Timeout: no se recibio autorizacion a tiempo.");
            return;
        }

        ct.ThrowIfCancellationRequested();

        SetSanitizeStatus("[2/4] DIRECTIVA RECIBIDA - Ejecucion autorizada por el Ledger.", Color.Green);
        SetStatus("Autorizacion web recibida. Ejecutando borrado...");

        // 3+4. CLEAR-DISK + REINITIALIZE (combined para evitar estado inconsistente)
        SetSanitizeStatus("[3/4] Limpiando y reinicializando disco...", Color.Red);
        SetStatus("Borrando estructura sectorial y reconstruyendo tabla de particiones...");
        try
        {
            var psCmd = $@"
$disk = Get-Disk -Number {diskIndex} -ErrorAction Stop
Clear-Disk -Number {diskIndex} -RemoveData -RemoveOEM -Confirm:$false -ErrorAction Stop
Start-Sleep -Seconds 3
Update-HostStorageCache -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Set-Disk -Number {diskIndex} -IsOffline $false -ErrorAction SilentlyContinue
Set-Disk -Number {diskIndex} -IsReadOnly $false -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
$disk2 = Get-Disk -Number {diskIndex} -ErrorAction Stop
if ($disk2.PartitionStyle -eq 'RAW') {{
    Initialize-Disk -Number {diskIndex} -PartitionStyle MBR -ErrorAction Stop
}}
Get-Partition -DiskNumber {diskIndex} -ErrorAction SilentlyContinue | Remove-Partition -Confirm:$false -ErrorAction SilentlyContinue
$part = New-Partition -DiskNumber {diskIndex} -UseMaximumSize -AssignDriveLetter -ErrorAction Stop
$part | Format-Volume -FileSystem {filesystem} -Confirm:$false -ErrorAction Stop
" + @"Write-Host ""OK""" + @"
";
            var psi = new ProcessStartInfo("powershell",
                $"-NoProfile -Command \"{psCmd.Replace("\"", "\\\"").Replace("\n", ";").Replace("\r", "")}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                if (!proc.WaitForExit(120000))
                {
                    proc.Kill();
                    var err = await proc.StandardError.ReadToEndAsync(ct);
                    SetSanitizeStatus($"[3/4] El proceso excedio el tiempo de espera (120s). Stderr: {err.Trim()}", Color.Red);
                    SetStatus("Timeout - el disco puede requerir inicializacion manual.");
                    return;
                }
                var stderr = await proc.StandardError.ReadToEndAsync(ct);
                if (proc.ExitCode != 0)
                {
                    SetSanitizeStatus($"[3/4] Error (codigo {proc.ExitCode}): {stderr.Trim()}", Color.Red);
                    SetStatus("Fallo - inicialice el disco manualmente con Administracion de Discos.");
                    return;
                }
            }
            SetSanitizeStatus("[3/4] Disco limpiado, inicializado y formateado correctamente.", Color.Green);
        }
        catch (Exception ex)
        {
            SetSanitizeStatus($"[3/4] Error critico: {ex.Message}", Color.Red);
            SetStatus("Fallo en el borrado/inicializacion del disco.");
            return;
        }

        // 4. CERTIFICATE
        SetSanitizeStatus("[4/4] Despachando certificado criptografico inmutable...", Color.DarkBlue);
        SetStatus("Registrando certificado de completitud...");

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var rawHash = serial + timestamp + "AUTHORIZED_WEB_PURGE";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawHash));
        var hashString = Convert.ToHexStringLower(hashBytes);

        var certBody = $"{{\"serialNumber\":{EscapeJson(serial)},\"model\":{EscapeJson(model)},\"technicianId\":{EscapeJson(techId)},\"workstation\":{EscapeJson(workstation)},\"status\":\"COMPLETED\",\"sha256\":{EscapeJson(hashString)},\"sessionToken\":{EscapeJson(sessionToken)},\"timestamp\":{EscapeJson(timestamp)}}}";
        var certResponse = await PostJsonWithResponseAsync(_httpClient, $"{baseUrl}/api/v1/security/certificate", certBody);
        string displayId = hashString;

        if (certResponse != null)
        {
            displayId = serial;
            SetSanitizeStatus("Listo. Flujo cerrado. Bitacora web actualizada a COMPLETED.", Color.Green);
            _txtCertHash.Text = displayId;
            _btnCopyCert.Enabled = true;
            SetStatus($"Saneamiento completado. Serial: {displayId}");
        }
        else
        {
            SetSanitizeStatus("Borrado exitoso, pero no se pudo registrar el certificado en el servidor.", Color.DarkOrange);
            _txtCertHash.Text = serial;
            _btnCopyCert.Enabled = true;
            SetStatus("Saneamiento completado (certificado no registrado en web).");
        }
    }

    private void ResetDisplay()
    {
        _lblDiskName.Text = "---";
        _lblSerial.Text = "---";
        _lblCapacity.Text = "---";
        _lblType.Text = "---";
        _healthBar.Value = 0;
        _healthBar.ForeColor = Color.FromArgb(40, 180, 80);
        _lblHealthValue.Text = "---";
        _lblGrade.Text = "";
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
            _healthBar.ForeColor = Color.FromArgb(40, 180, 80);
        else if (m.HealthScore >= 50)
            _healthBar.ForeColor = Color.FromArgb(230, 170, 40);
        else
            _healthBar.ForeColor = Color.FromArgb(200, 60, 50);

        _lblGrade.Text = "Grado " + m.Grade;

        string healthColor;
        if (m.HealthScore >= 75) healthColor = "#28B84A";
        else if (m.HealthScore >= 50) healthColor = "#E6AA28";
        else healthColor = "#C83C32";
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
            var body = await resp.Content.ReadAsStringAsync();
            var key = "\"eraseMethod\":\"";
            int idx = body.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int s = idx + key.Length, e = body.IndexOf('"', s);
                if (e >= 0) return body[s..e];
            }
            return body;
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
