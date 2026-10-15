using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Overlay;

// Prova de um clique DENTRO do jogo (rode por provar-no-jogo.bat, que pede admin). Durante 30 s, com o jogo na frente:
// - FPS por dois métodos independentes ao mesmo tempo: PresentMon (eventos do Windows) e ContadorTela (quadros na tela);
//   se os dois batem, o número é confiável. Se o anticheat bloquear o PresentMon, diz isso.
// - Ping: detecta o servidor da partida (eventos de rede) e compara o ping do overlay com o ping.exe do Windows.
// Usa sessões ETW com nomes próprios: não derruba o overlay instalado, que pode continuar aberto.
// Resultado na tela e em .tmp\prova-jogo.txt (o Claude lê esse arquivo).
static class ProvaJogo
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr janela, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string dispositivo, int modo, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    static readonly StringBuilder relatorio = new StringBuilder();

    static void Dizer(string texto)
    {
        Console.WriteLine(texto);
        relatorio.AppendLine(texto);
    }

    static int Main(string[] args)
    {
        Console.Title = "Prova do overlay no jogo";
        // --ensaio <processo>: só para o Claude conferir o fluxo sem admin, mirando a janela de um processo, por 8 s.
        bool ensaio = args.Length >= 2 && args[0] == "--ensaio";
        int duracao = ensaio ? 8 : 30;
        using (var id = WindowsIdentity.GetCurrent())
            if (!ensaio && !new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Console.WriteLine("Rode pelo provar-no-jogo.bat (precisa de administrador).");
                return 1;
            }

        IntPtr janela;
        if (ensaio)
        {
            Process[] processoAlvo = Process.GetProcessesByName(args[1]);
            janela = processoAlvo.Length > 0 ? processoAlvo[0].MainWindowHandle : GetForegroundWindow();
        }
        else
        {
            Console.WriteLine("VOLTE PARA O JOGO AGORA (clique nele). A prova comeca em 10 s e dura 30 s.");
            Console.WriteLine("Fique jogando normal (de preferencia numa partida). Nao precisa fazer mais nada.");
            for (int s = 10; s > 0; s--) { Console.Write(s + "... "); Thread.Sleep(1000); }
            Console.WriteLine();
            janela = GetForegroundWindow();
        }
        uint pidU;
        GetWindowThreadProcessId(janela, out pidU);
        int pid = (int)pidU;
        string nomeJogo = "?";
        try { nomeJogo = Process.GetProcessById(pid).ProcessName; } catch (ArgumentException) { }
        int hz = TaxaDoMonitor(janela);

        Dizer("Prova do overlay no jogo - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Dizer("Janela na frente: " + nomeJogo + " (pid " + pid + "), monitor de " + hz + " Hz");
        if (pid == Process.GetCurrentProcess().Id || nomeJogo == "conhost" || nomeJogo == "WindowsTerminal" || nomeJogo == "explorer")
            Dizer("ATENCAO: a janela da frente nao parece ser um jogo. Rode de novo e clique no jogo durante a contagem.");

        var relogio = Stopwatch.StartNew();
        var leitor = new LeitorFps("OverlayProvaFps");
        var contador = new ContadorTela(() => janela);
        var detector = new DetectorServidor();
        var monitor = new MonitorRede(detector, relogio, "OverlayProvaRede");
        var ping = new MedidorPing("1.1.1.1", () => detector.Servidor(relogio.Elapsed.TotalMilliseconds));

        Rectangle overlayInstalado = JanelaDoOverlayInstalado();
        if (!overlayInstalado.IsEmpty) contador.DefinirAreaIgnorada(overlayInstalado);
        leitor.Iniciar();
        leitor.DefinirProcessoAlvo(pid);
        detector.DefinirAlvo(pid);
        contador.Iniciar();
        monitor.Iniciar();
        ping.Iniciar();

        var fpsPresentMon = new List<int>();
        var fpsTela = new List<int>();
        var pingServidor = new List<int>();
        var pingReserva = new List<int>();
        IPAddress servidor = null;
        int segundosForaDoJogo = 0;
        Console.WriteLine("Medindo " + duracao + " s...");
        Thread.Sleep(3000); // aquecimento
        for (int s = 0; s < duracao; s++)
        {
            Thread.Sleep(1000);
            uint frente;
            GetWindowThreadProcessId(GetForegroundWindow(), out frente);
            if ((int)frente != pid && !ensaio) segundosForaDoJogo++;
            int? a = leitor.Fps();
            int? b = contador.Fps();
            if (a.HasValue) fpsPresentMon.Add(a.Value);
            if (b.HasValue) fpsTela.Add(b.Value);
            IPAddress srv = detector.Servidor(relogio.Elapsed.TotalMilliseconds);
            if (srv != null) servidor = srv;
            int? ms;
            OrigemPing origem;
            ping.Ler(out ms, out origem);
            if (ms.HasValue) (origem == OrigemPing.ServidorDoJogo ? pingServidor : pingReserva).Add(ms.Value);
            Console.Write("\r{0,2}s  PresentMon: {1,4}  Tela: {2,4}  Ping: {3,4} ms {4}      ", s + 1,
                a.HasValue ? a.Value.ToString() : "---", b.HasValue ? b.Value.ToString() : "---",
                ms.HasValue ? ms.Value.ToString() : "---", origem == OrigemPing.ServidorDoJogo ? "(servidor)" : "(internet)");
        }
        Console.WriteLine();
        bool presentMonBloqueado = leitor.Bloqueado;
        EstadoFps estadoPresentMon = leitor.Estado;
        leitor.Dispose();
        contador.Dispose();
        monitor.Dispose();
        ping.Dispose();

        Dizer("");
        if (segundosForaDoJogo > 3) Dizer("ATENCAO: o jogo saiu da frente por " + segundosForaDoJogo + " s; a prova pode ter medido outra coisa.");
        int falhas = 0;

        // FPS: os dois métodos são independentes; se batem, o número está certo.
        Dizer("== FPS ==");
        if (estadoPresentMon == EstadoFps.SemAdmin) Dizer("PresentMon: nao rodou (sem administrador).");
        else if (estadoPresentMon == EstadoFps.SemPresentMon) Dizer("PresentMon: nao encontrado ou com hash errado (rode tools\\baixar-presentmon.ps1).");
        else if (presentMonBloqueado) Dizer("PresentMon: BLOQUEADO pelo Windows (anticheat). O overlay usa o FPS pela tela (FPS*).");
        else if (fpsPresentMon.Count == 0) Dizer("PresentMon: sem dados (o jogo nao apresentou quadros pelo DXGI/D3D?).");
        else Dizer(string.Format("PresentMon (metodo normal): mediana {0}  (min {1}, max {2})", Mediana(fpsPresentMon), Min(fpsPresentMon), Max(fpsPresentMon)));
        if (fpsTela.Count == 0) Dizer("Tela (FPS*): sem dados.");
        else Dizer(string.Format("Tela (FPS*):               mediana {0}  (min {1}, max {2})", Mediana(fpsTela), Min(fpsTela), Max(fpsTela)));

        if (fpsPresentMon.Count > 0 && fpsTela.Count > 0)
        {
            int pm = Mediana(fpsPresentMon), tela = Mediana(fpsTela);
            bool ok;
            if (hz > 0 && pm >= hz - 3)
            {
                // Jogo acima da taxa do monitor: a tela só mostra até hz.
                ok = tela >= hz * 0.95;
                Dizer(string.Format("Comparacao: jogo a {0} FPS, acima dos {1} Hz do monitor; a tela mostra {2} (o maximo possivel). {3}",
                    pm, hz, tela, ok ? "OK" : "FALHA"));
            }
            else
            {
                int diferenca = tela - pm;
                ok = Math.Abs(diferenca) <= Math.Max(2, pm * 0.03);
                Dizer(string.Format("Comparacao: PresentMon {0} x Tela {1} -> diferenca {2:+0;-0;0}. {3}", pm, tela, diferenca,
                    ok ? "OK, os dois metodos batem" : "FALHA, os metodos discordam"));
            }
            if (!ok) falhas++;
        }

        // Ping: servidor da partida (se detectado) contra o ping.exe no mesmo IP.
        Dizer("");
        Dizer("== PING ==");
        if (servidor == null)
            Dizer("Servidor da partida: nao detectado (fora de partida, ou eventos de rede bloqueados pelo anticheat). Overlay mostra ms*.");
        else
        {
            Dizer("Servidor da partida detectado: " + servidor);
            if (pingServidor.Count == 0) Dizer("O servidor nao responde a ping (comum em servidores de jogo). Overlay mostra ms* (internet).");
        }
        string alvo = servidor != null && pingServidor.Count > 0 ? servidor.ToString() : "1.1.1.1";
        List<int> doOverlay = servidor != null && pingServidor.Count > 0 ? pingServidor : pingReserva;
        List<int> doWindows = PingWindows(alvo, 10);
        if (doOverlay.Count > 0 && doWindows.Count > 0)
        {
            int o = Mediana(doOverlay), w = Mediana(doWindows);
            bool ok = Math.Abs(o - w) <= Math.Max(3, w * 0.15);
            if (!ok) falhas++;
            Dizer(string.Format("Ping {0}: overlay {1} ms x ping do Windows {2} ms -> {3}", alvo == "1.1.1.1" ? "da internet (1.1.1.1)" : "do servidor " + alvo,
                o, w, ok ? "OK" : "FALHA"));
        }
        else Dizer("Ping: sem leituras suficientes para comparar.");

        Dizer("");
        Dizer(falhas == 0 ? "RESULTADO: tudo OK" : "RESULTADO: " + falhas + " falha(s)");
        try
        {
            string pasta = Path.Combine(Pastas.Programa, ".tmp");
            Directory.CreateDirectory(pasta);
            File.WriteAllText(Path.Combine(pasta, ensaio ? "prova-jogo-ensaio.txt" : "prova-jogo.txt"), relatorio.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Resultado salvo para o Claude.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Nao consegui salvar o resultado: " + ex.Message);
        }
        if (!ensaio)
        {
            Console.WriteLine("Aperte qualquer tecla para fechar.");
            Console.ReadKey(true);
        }
        return falhas == 0 ? 0 : 1;
    }

    static int TaxaDoMonitor(IntPtr janela)
    {
        var modo = new DEVMODE();
        modo.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        string dispositivo = Screen.FromHandle(janela).DeviceName;
        return EnumDisplaySettings(dispositivo, -1 /* ENUM_CURRENT_SETTINGS */, ref modo) ? modo.dmDisplayFrequency : 0;
    }

    static Rectangle JanelaDoOverlayInstalado()
    {
        foreach (Process p in Process.GetProcessesByName("Overlay"))
        {
            // A janela do overlay é "tool window"; Screen/Bounds dela vêm do canto configurado. Basta o retângulo visível.
            Rectangle r = Rectangle.Empty;
            Native2.EnumerarJanelas((uint)p.Id, ref r);
            if (!r.IsEmpty) return r;
        }
        return Rectangle.Empty;
    }

    static List<int> PingWindows(string alvo, int vezes)
    {
        var tempos = new List<int>();
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n " + vezes + " " + alvo)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using (Process p = Process.Start(info))
        {
            string saida = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (Match m in Regex.Matches(saida, @"(?:tempo|time)[=<](\d+)ms")) tempos.Add(int.Parse(m.Groups[1].Value));
        }
        return tempos;
    }

    static int Mediana(List<int> v) { var c = new List<int>(v); c.Sort(); return c[c.Count / 2]; }
    static int Min(List<int> v) { int m = int.MaxValue; foreach (int x in v) m = Math.Min(m, x); return m; }
    static int Max(List<int> v) { int m = int.MinValue; foreach (int x in v) m = Math.Max(m, x); return m; }
}

static class Native2
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    public static void EnumerarJanelas(uint alvo, ref Rectangle resultado)
    {
        Rectangle achado = Rectangle.Empty;
        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            RECT r;
            if (pid == alvo && IsWindowVisible(h) && GetWindowRect(h, out r)) achado = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            return true;
        }, IntPtr.Zero);
        resultado = achado;
    }
}
