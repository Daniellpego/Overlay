using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Overlay
{
    class OverlayForm : Form
    {
        const int PaddingX = 8;
        const int PaddingY = 4;
        const int RaioBorda = 6;

        Config config;
        readonly Timer timer;
        readonly NotifyIcon bandeja;
        readonly ToolStripMenuItem itemMostrar;
        readonly LeitorFps leitorFps = new LeitorFps();
        readonly Stopwatch relogio = Stopwatch.StartNew();
        readonly DetectorServidor detectorServidor = new DetectorServidor();
        readonly MedidorPing medidorPing;
        MonitorRede monitorRede;
        string ultimoTexto;
        int ticksDesdeTopo;

        public OverlayForm(Config config)
        {
            this.config = config;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            leitorFps.Iniciar();

            bool detectar = config.DetectarServidorJogo && LeitorFps.EhAdministrador();
            if (detectar)
            {
                monitorRede = new MonitorRede(detectorServidor, relogio);
                if (!monitorRede.Iniciar()) monitorRede = null;
            }
            medidorPing = new MedidorPing(config.AlvoPing,
                monitorRede == null ? (Func<System.Net.IPAddress>)null : () => detectorServidor.Servidor(relogio.Elapsed.TotalMilliseconds));
            medidorPing.Iniciar();

            itemMostrar = new ToolStripMenuItem("Ocultar overlay", null, (s, e) => AlternarVisivel());
            var menu = new ContextMenuStrip();
            menu.Items.Add(itemMostrar);
            if (leitorFps.Estado == EstadoFps.SemAdmin)
                menu.Items.Add("Reiniciar como administrador (FPS)", null, (s, e) => ReiniciarComoAdmin());
            menu.Items.Add("Recarregar config.ini", null, (s, e) => RecarregarConfig());
            menu.Items.Add("Abrir config.ini", null, (s, e) => AbrirConfig());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, (s, e) => Close());

            bandeja = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Overlay (horário, ping, FPS)",
                ContextMenuStrip = menu,
                Visible = true
            };

            // 500 ms basta: só redesenhamos quando o texto muda, então o custo real é ~1 redesenho por minuto (ou por segundo).
            timer = new Timer { Interval = 500 };
            timer.Tick += (s, e) => Tick();

            SystemEvents.DisplaySettingsChanged += AoMudarTela;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // Click-through (TRANSPARENT), fora do Alt+Tab (TOOLWINDOW) e nunca rouba o foco do jogo (NOACTIVATE).
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                              Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        // Primeiro desenho só depois que o WinForms termina de posicionar a janela; antes disso ele sobrescreve tamanho e posição.
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Redesenhar(true);
            timer.Start();

            if (leitorFps.Estado == EstadoFps.SemAdmin)
                bandeja.ShowBalloonTip(5000, "Overlay sem FPS",
                    "Para medir o FPS, abra pelo iniciar.bat (pede administrador) ou use o menu do ícone.", ToolTipIcon.Info);
            else if (leitorFps.Estado == EstadoFps.SemPresentMon)
                bandeja.ShowBalloonTip(5000, "Overlay sem FPS",
                    "PresentMon não encontrado. Rode tools\\baixar-presentmon.ps1.", ToolTipIcon.Warning);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= AoMudarTela;
            timer.Stop();
            leitorFps.Dispose();
            medidorPing.Dispose();
            if (monitorRede != null) monitorRede.Dispose();
            bandeja.Visible = false;
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                bandeja.Dispose();
                leitorFps.Dispose();
                medidorPing.Dispose();
                if (monitorRede != null) monitorRede.Dispose();
            }
            base.Dispose(disposing);
        }

        void Tick()
        {
            if (!Visible) return;
            int pidFrente = Native.ProcessoEmPrimeiroPlano();
            leitorFps.DefinirProcessoAlvo(pidFrente);
            detectorServidor.DefinirAlvo(pidFrente);
            Redesenhar(false);

            // Alguns jogos em tela cheia sem borda se colocam por cima; reafirmar o topo a cada ~2 s é barato.
            if (++ticksDesdeTopo >= 4)
            {
                ticksDesdeTopo = 0;
                Native.ManterNoTopo(Handle);
            }
        }

        string MontarTexto()
        {
            string hora = DateTime.Now.ToString(config.MostrarSegundos ? "HH:mm:ss" : "HH:mm");
            return TextoFps() + " FPS | " + TextoPing() + " | " + hora;
        }

        string TextoPing()
        {
            int? ms;
            OrigemPing origem;
            medidorPing.Ler(out ms, out origem);
            if (!ms.HasValue) return "--- ms";
            // "*" = não é o servidor da partida (sem partida detectada ou servidor que bloqueia ping): é o alvo_ping do config.
            return Math.Min(ms.Value, 999).ToString().PadLeft(3) + (origem == OrigemPing.ServidorDoJogo ? " ms" : " ms*");
        }

        string TextoFps()
        {
            if (leitorFps.Estado == EstadoFps.SemAdmin) return "ADM";
            int? fps = leitorFps.Fps();
            // Largura fixa de 3 caracteres para o overlay não "pular" quando o FPS muda de 99 para 100.
            return fps.HasValue ? Math.Min(fps.Value, 999).ToString().PadLeft(3) : "---";
        }

        void ReiniciarComoAdmin()
        {
            var info = new ProcessStartInfo(Application.ExecutablePath, Program.ArgumentoReiniciando)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                Process.Start(info);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return; // UAC recusado: continua rodando como está.
            }
            Close();
        }

        void Redesenhar(bool forcar)
        {
            string texto = MontarTexto();
            if (!forcar && texto == ultimoTexto) return;
            ultimoTexto = texto;

            using (var fonte = new Font(config.Fonte, config.TamanhoFonte, FontStyle.Bold, GraphicsUnit.Point))
            using (var formato = new StringFormat(StringFormat.GenericTypographic))
            {
                Size medida;
                using (var medidor = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(medidor))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    medida = Size.Ceiling(g.MeasureString(texto, fonte, PointF.Empty, formato));
                }
                int largura = medida.Width + PaddingX * 2;
                int altura = medida.Height + PaddingY * 2;

                using (var bmp = new Bitmap(largura, altura, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                        using (var fundo = new SolidBrush(Color.FromArgb(config.OpacidadeFundo, 0, 0, 0)))
                        using (var forma = Retangulo(new Rectangle(0, 0, largura - 1, altura - 1), RaioBorda))
                            g.FillPath(fundo, forma);

                        using (var pincel = new SolidBrush(config.CorTexto))
                            g.DrawString(texto, fonte, pincel, PaddingX, PaddingY, formato);
                    }
                    var limites = new Rectangle(Posicao(largura, altura), bmp.Size);
                    // Mantém o WinForms com os mesmos limites; senão um Show()/Hide() posterior volta a janela ao tamanho padrão.
                    if (Bounds != limites) Bounds = limites;
                    Native.DesenharJanela(Handle, bmp, limites.Location);
                }
            }
        }

        Point Posicao(int largura, int altura)
        {
            Screen[] telas = Screen.AllScreens;
            Rectangle area = (config.Monitor < telas.Length ? telas[config.Monitor] : Screen.PrimaryScreen).Bounds;

            int x = config.Canto.EndsWith("esquerdo") ? area.Left + config.MargemX : area.Right - largura - config.MargemX;
            int y = config.Canto.StartsWith("superior") ? area.Top + config.MargemY : area.Bottom - altura - config.MargemY;
            return new Point(x, y);
        }

        static GraphicsPath Retangulo(Rectangle r, int raio)
        {
            int d = raio * 2;
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void AlternarVisivel()
        {
            if (Visible)
            {
                Hide();
                itemMostrar.Text = "Mostrar overlay";
            }
            else
            {
                Show();
                Redesenhar(true);
                itemMostrar.Text = "Ocultar overlay";
            }
        }

        void RecarregarConfig()
        {
            config = Config.Carregar();
            medidorPing.DefinirAlvoReserva(config.AlvoPing);
            Redesenhar(true);
        }

        void AbrirConfig()
        {
            if (!System.IO.File.Exists(Config.Caminho)) return;
            Process.Start("notepad.exe", "\"" + Config.Caminho + "\"");
        }

        void AoMudarTela(object sender, EventArgs e)
        {
            Redesenhar(true);
        }
    }
}
