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
        string ultimoTexto;
        int ticksDesdeTopo;

        public OverlayForm(Config config)
        {
            this.config = config;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            itemMostrar = new ToolStripMenuItem("Ocultar overlay", null, (s, e) => AlternarVisivel());
            var menu = new ContextMenuStrip();
            menu.Items.Add(itemMostrar);
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
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= AoMudarTela;
            timer.Stop();
            bandeja.Visible = false;
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                bandeja.Dispose();
            }
            base.Dispose(disposing);
        }

        void Tick()
        {
            if (!Visible) return;
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
            // FPS e ping entram nas etapas 2 e 3.
            return "--- FPS | -- ms | " + hora;
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
