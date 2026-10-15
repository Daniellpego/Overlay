using System;
using System.Collections.Generic;
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
        const int IdAtalhoOcultar = 1;

        Config config;
        readonly Timer timer;
        readonly NotifyIcon bandeja;
        readonly ToolStripMenuItem itemMostrar;
        readonly ToolStripMenuItem itemInicioAutomatico;
        bool atalhoRegistrado;
        readonly LeitorFps leitorFps = new LeitorFps();
        ContadorTela contadorTela;
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
            itemInicioAutomatico = new ToolStripMenuItem("Iniciar com o Windows", null, (s, e) => AlternarInicioAutomatico());
            menu.Items.Add(itemInicioAutomatico);
            menu.Opening += (s, e) => AtualizarItemInicioAutomatico();
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
            RegistrarAtalho();

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
            DesregistrarAtalho();
            leitorFps.Dispose();
            medidorPing.Dispose();
            if (monitorRede != null) monitorRede.Dispose();
            if (contadorTela != null) contadorTela.Dispose();
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
                if (contadorTela != null) contadorTela.Dispose();
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

        struct Trecho
        {
            public string Texto;
            public Color Cor;
            public Trecho(string texto, Color cor) { Texto = texto; Cor = cor; }
        }

        List<Trecho> MontarTrechos()
        {
            bool pelaTela;
            int? fps = LerFps(out pelaTela);
            int? ms;
            OrigemPing origem;
            medidorPing.Ler(out ms, out origem);
            string hora = DateTime.Now.ToString(config.MostrarSegundos ? "HH:mm:ss" : "HH:mm");

            return new List<Trecho>
            {
                new Trecho(pelaTela ? TextoNumero(fps) : TextoFps(fps), Faixas.CorFps(fps, config)),
                // "FPS*" = medido pela tela (plano B): conta o que chega ao monitor, com teto na taxa de atualização dele.
                new Trecho(pelaTela ? " FPS* | " : " FPS | ", config.CorTexto),
                new Trecho(TextoPing(ms, origem), Faixas.CorPing(ms, config)),
                new Trecho(" | " + hora, config.CorTexto)
            };
        }

        static string TextoPing(int? ms, OrigemPing origem)
        {
            if (!ms.HasValue) return "--- ms";
            // "*" = não é o servidor da partida (sem partida detectada ou servidor que bloqueia ping): é o alvo_ping do config.
            return Math.Min(ms.Value, 999).ToString().PadLeft(3) + (origem == OrigemPing.ServidorDoJogo ? " ms" : " ms*");
        }

        // PresentMon (FPS do jogo em primeiro plano) normalmente. Quando o Windows recusa a sessão dele mesmo como admin
        // (anticheat do FC 27) e fps_pela_tela = sim, usa o ContadorTela. Ele só existe enquanto o bloqueio durar
        // (custa ~20-30 MB de RAM do Direct3D e ~0,5% de CPU).
        int? LerFps(out bool pelaTela)
        {
            pelaTela = leitorFps.Bloqueado && config.FpsPelaTela;
            if (!pelaTela)
            {
                if (contadorTela != null) { contadorTela.Dispose(); contadorTela = null; }
                return leitorFps.Estado == EstadoFps.SemAdmin ? null : leitorFps.Fps();
            }
            if (contadorTela == null)
            {
                contadorTela = new ContadorTela();
                contadorTela.DefinirAreaIgnorada(Bounds);
                contadorTela.Iniciar();
            }
            return contadorTela.Fps();
        }

        string TextoFps(int? fps)
        {
            if (leitorFps.Estado == EstadoFps.SemAdmin) return "ADM";
            // BLQ = o Windows recusou a leitura de quadros mesmo como admin (ex.: anticheat do FC 27 aberto).
            if (leitorFps.Bloqueado && !fps.HasValue) return "BLQ";
            return TextoNumero(fps);
        }

        // Largura fixa de 3 caracteres para o overlay não "pular" quando o FPS muda de 99 para 100.
        static string TextoNumero(int? fps)
        {
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
            List<Trecho> trechos = MontarTrechos();
            var chave = new System.Text.StringBuilder();
            foreach (Trecho t in trechos) chave.Append(t.Texto).Append('|').Append(t.Cor.ToArgb()).Append(';');
            if (!forcar && chave.ToString() == ultimoTexto) return;
            ultimoTexto = chave.ToString();

            using (var fonte = new Font(config.Fonte, config.TamanhoFonte, FontStyle.Bold, GraphicsUnit.Point))
            using (var formato = new StringFormat(StringFormat.GenericTypographic))
            {
                // Sem MeasureTrailingSpaces o GDI+ ignora os espaços no fim de cada trecho e os pedaços se sobrepõem.
                formato.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

                var larguras = new float[trechos.Count];
                float larguraTexto = 0, alturaTexto = 0;
                using (var medidor = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(medidor))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    for (int i = 0; i < trechos.Count; i++)
                    {
                        SizeF tamanho = g.MeasureString(trechos[i].Texto, fonte, PointF.Empty, formato);
                        larguras[i] = tamanho.Width;
                        larguraTexto += tamanho.Width;
                        alturaTexto = Math.Max(alturaTexto, tamanho.Height);
                    }
                }
                int largura = (int)Math.Ceiling(larguraTexto) + PaddingX * 2;
                int altura = (int)Math.Ceiling(alturaTexto) + PaddingY * 2;

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

                        float x = PaddingX;
                        for (int i = 0; i < trechos.Count; i++)
                        {
                            using (var pincel = new SolidBrush(trechos[i].Cor))
                                g.DrawString(trechos[i].Texto, fonte, pincel, x, PaddingY, formato);
                            x += larguras[i];
                        }
                    }
                    var limites = new Rectangle(Posicao(largura, altura), bmp.Size);
                    // Mantém o WinForms com os mesmos limites; senão um Show()/Hide() posterior volta a janela ao tamanho padrão.
                    if (Bounds != limites) Bounds = limites;
                    Native.DesenharJanela(Handle, bmp, limites.Location);
                    // Mudanças só na área do overlay não contam como quadro do jogo.
                    if (contadorTela != null) contadorTela.DefinirAreaIgnorada(limites);
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
            DesregistrarAtalho();
            RegistrarAtalho();
            Redesenhar(true);
        }

        void AbrirConfig()
        {
            if (!System.IO.File.Exists(Config.Caminho)) return;
            // Caminho absoluto: "notepad.exe" sozinho seria procurado primeiro na pasta do overlay.
            Process.Start(System.IO.Path.Combine(Environment.SystemDirectory, "notepad.exe"), "\"" + Config.Caminho + "\"");
        }

        void AoMudarTela(object sender, EventArgs e)
        {
            Redesenhar(true);
        }

        void RegistrarAtalho()
        {
            string texto = config.AtalhoOcultar.Trim();
            if (texto.Length == 0 || texto.ToLowerInvariant() == "nenhum") return;

            uint modificadores, tecla;
            if (!Atalho.TentarLer(texto, out modificadores, out tecla))
            {
                Log.Mensagem("atalho_ocultar invalido: '" + texto + "'. Use algo como Ctrl+Shift+F10.");
                return;
            }
            atalhoRegistrado = Native.RegisterHotKey(Handle, IdAtalhoOcultar, modificadores, tecla);
            if (!atalhoRegistrado)
                Log.Mensagem("Nao foi possivel registrar o atalho '" + texto + "' (outro programa ja usa?). Erro " +
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ".");
        }

        void DesregistrarAtalho()
        {
            if (!atalhoRegistrado) return;
            Native.UnregisterHotKey(Handle, IdAtalhoOcultar);
            atalhoRegistrado = false;
        }

        // RegisterHotKey é tratado pelo próprio Windows: funciona com o jogo em foco, sem gancho de teclado nem injeção.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == IdAtalhoOcultar)
            {
                AlternarVisivel();
                return;
            }
            base.WndProc(ref m);
        }

        void AtualizarItemInicioAutomatico()
        {
            bool ativo = InicioAutomatico.EstaAtivo();
            itemInicioAutomatico.Checked = ativo;
            // Fora do Program Files dá para desligar uma tarefa antiga, mas não criar uma nova.
            itemInicioAutomatico.Enabled = LeitorFps.EhAdministrador() && (Pastas.Instalado || ativo);
            if (!Pastas.Instalado)
                itemInicioAutomatico.ToolTipText = "Só a cópia instalada (tools\\instalar.ps1) pode iniciar com o Windows.";
            else if (!LeitorFps.EhAdministrador())
                itemInicioAutomatico.ToolTipText = "Abra o overlay pelo iniciar.bat (administrador) para mudar esta opção.";
            else
                itemInicioAutomatico.ToolTipText = "";
        }

        void AlternarInicioAutomatico()
        {
            bool ok = InicioAutomatico.EstaAtivo()
                ? InicioAutomatico.Desativar()
                : InicioAutomatico.Ativar(Application.ExecutablePath);
            if (!ok)
                bandeja.ShowBalloonTip(5000, "Overlay", "Não foi possível mudar o início automático. Detalhes em overlay.log.", ToolTipIcon.Warning);
        }
    }
}
