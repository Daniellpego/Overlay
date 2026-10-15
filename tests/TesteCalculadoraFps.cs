using System;
using System.Globalization;
using Overlay;

// Testes da CalculadoraFps com CSV sintético no formato do PresentMon 2.x. Compilado e rodado por tests\verificar.ps1.
static class TesteCalculadoraFps
{
    const string Cabecalho = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,CPUStartTime,MsCPUBusy,MsCPUWait";
    static int falhas;

    static int Main()
    {
        GeraFpsConstante(60);
        GeraFpsConstante(144);
        GeraFpsConstante(240);
        IgnoraOutrosProcessos();
        EscolheSwapchainPrincipal();
        SemDadosRecentesDaNulo();
        TrocaDeAlvoLimpaDados();
        LinhasQuebradasNaoDerrubam();
        CabecalhoSemColunasEsperadas();
        ReinicioDoPresentMonZeraTempo();
        RajadaAuxiliarNaoViraFpsDoJogo();
        FormatosReaisDoPresentMon();
        ColunaExplicitaEmMsTemPreferencia();
        SuperficiesEncerradasSaoLiberadas();
        PidsInvalidosNaoEntramNaAmostra();

        Console.WriteLine(falhas == 0 ? "Calculo de FPS: tudo OK" : "Calculo de FPS: " + falhas + " falha(s)");
        return falhas == 0 ? 0 : 1;
    }

    static void SuperficiesEncerradasSaoLiberadas()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        for (int i = 0; i < 100; i++) c.ProcessarLinha(Linha("Jogo.exe", 1234, "encerrada" + i, i * 1000), i * 1000);
        Alimentar(c, "Jogo.exe", 1234, "atual", 60, 100000, 2000);
        var campo = typeof(CalculadoraFps).GetField("framesPorSwapchain", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var filas = (System.Collections.IDictionary)campo.GetValue(c);
        Checar("superficies encerradas nao acumulam filas em memoria", filas.Count <= 2 && c.Fps(102000) == 60, "(filas " + filas.Count + ")");
    }

    static void PidsInvalidosNaoEntramNaAmostra()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        foreach (string pid in new[] { "", "-1234", "2147483648", "999999999999999", "12x34", "+" })
            c.ProcessarLinha("Jogo.exe," + pid + ",0x1,DXGI,0,0,1000,1,1", 1000);
        Checar("PID invalido ou com overflow e descartado", !c.Fps(1000).HasValue, "");
        for (double t = 0; t < 2000; t += 10)
            c.ProcessarLinha("Jogo.exe, +1234 ,0x1,DXGI,0,0," + t.ToString(CultureInfo.InvariantCulture) + ",1,1", t);
        Checar("PID decimal aceita espacos e sinal positivo", c.Fps(2000) == 100, "");
    }

    static void FormatosReaisDoPresentMon()
    {
        // Cabecalho observado no overlay.log em 15/10/2026, com PresentMon 2.6.0 fixado no pacote.
        const string real = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,TimeInMs,MsBetweenSimulationStart,MsBetweenPresents,MsInPresentAPI,MsRenderPresentLatency,CPUStartTimeInMs,MsBetweenAppStart,MsCPUBusy,MsCPUWait";
        foreach (int alvo in new[] { 30, 60, 144, 240 })
        {
            var c = new CalculadoraFps();
            c.ProcessarLinha(real, 0);
            c.DefinirAlvo(1234);
            for (double t = 0; t < 2000; t += 1000.0 / alvo)
            {
                string linha = "FC27.exe,1234,0x1,DXGI,0,0," + (t * 2).ToString("F4", CultureInfo.InvariantCulture) +
                    ",0,16.6667,0,0," + t.ToString("F4", CultureInfo.InvariantCulture) + ",0,1,1";
                c.ProcessarLinha(linha, t);
            }
            Checar("cabecalho real CPUStartTimeInMs: " + alvo + " FPS", c.ErroCabecalho == null && c.Fps(2000) == alvo, "(obtido " + c.Fps(2000) + ")");
        }
        var legado = new CalculadoraFps();
        legado.ProcessarLinha("Application,ProcessID,SwapChainAddress,TimeInMs", 0);
        legado.DefinirAlvo(1234);
        for (double t = 0; t < 2000; t += 1000.0 / 75)
            legado.ProcessarLinha("Jogo.exe,1234,0x1," + t.ToString("F4", CultureInfo.InvariantCulture), t);
        Checar("TimeInMs legado permanece em milissegundos", legado.Fps(2000) == 75, "");
    }

    static void ColunaExplicitaEmMsTemPreferencia()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha("Application,ProcessID,SwapChainAddress,CPUStartTime,CPUStartTimeInMs,TimeInMs", 0);
        c.DefinirAlvo(1234);
        for (double t = 0; t < 2000; t += 10)
            c.ProcessarLinha("Jogo.exe,1234,0x1," + (t * 2).ToString(CultureInfo.InvariantCulture) + "," +
                t.ToString(CultureInfo.InvariantCulture) + "," + (t * 3).ToString(CultureInfo.InvariantCulture), t);
        Checar("CPUStartTimeInMs tem prioridade entre colunas validas", c.Fps(2000) == 100, "");
    }

    static void RajadaAuxiliarNaoViraFpsDoJogo()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        Alimentar(c, "Jogo.exe", 1234, "principal", 60, 0, 2000);
        c.ProcessarLinha(Linha("Jogo.exe", 1234, "auxiliar", 1998), 2000);
        c.ProcessarLinha(Linha("Jogo.exe", 1234, "auxiliar", 1999), 2000);
        Checar("rajada auxiliar nao substitui 60 FPS por 1000", c.Fps(2000) == 60, "(obtido " + c.Fps(2000) + ")");
    }

    static string Linha(string app, int pid, string swapchain, double tempoMs)
    {
        return app + "," + pid + "," + swapchain + ",DXGI,0,0," + tempoMs.ToString("F4", CultureInfo.InvariantCulture) + ",1.0,1.0";
    }

    static void Alimentar(CalculadoraFps c, string app, int pid, string swapchain, double fps, double inicioMs, double duracaoMs)
    {
        double passo = 1000.0 / fps;
        for (double t = inicioMs; t < inicioMs + duracaoMs; t += passo)
            c.ProcessarLinha(Linha(app, pid, swapchain, t), t);
    }

    static void Checar(string nome, bool ok, string detalhe)
    {
        Console.WriteLine((ok ? "[OK]    " : "[FALHA] ") + nome + " " + detalhe);
        if (!ok) falhas++;
    }

    static void GeraFpsConstante(int alvo)
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        Alimentar(c, "Jogo.exe", 1234, "0x1", alvo, 0, 3000);
        int? fps = c.Fps(3000);
        Checar("fps constante " + alvo, fps.HasValue && Math.Abs(fps.Value - alvo) <= 1, "(obtido " + fps + ")");
    }

    static void IgnoraOutrosProcessos()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        double passoJogo = 1000.0 / 90, passoNavegador = 1000.0 / 165;
        double tJogo = 0, tNav = 0;
        while (tJogo < 3000)
        {
            if (tNav <= tJogo) { c.ProcessarLinha(Linha("chrome.exe", 999, "0x9", tNav), tNav); tNav += passoNavegador; }
            else { c.ProcessarLinha(Linha("Jogo.exe", 1234, "0x1", tJogo), tJogo); tJogo += passoJogo; }
        }
        int? fps = c.Fps(3000);
        Checar("ignora outros processos", fps.HasValue && Math.Abs(fps.Value - 90) <= 1, "(obtido " + fps + ")");
    }

    static void EscolheSwapchainPrincipal()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        Alimentar(c, "Jogo.exe", 1234, "0xLAUNCHER", 30, 0, 3000);
        Alimentar(c, "Jogo.exe", 1234, "0xJOGO", 120, 0, 3000);
        int? fps = c.Fps(3000);
        Checar("usa a swapchain com mais frames", fps.HasValue && Math.Abs(fps.Value - 120) <= 1, "(obtido " + fps + ")");
    }

    static void SemDadosRecentesDaNulo()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        Alimentar(c, "Jogo.exe", 1234, "0x1", 60, 0, 1000);
        int? fps = c.Fps(1000 + CalculadoraFps.ValidadeMs + 1);
        Checar("sem dados recentes mostra ---", !fps.HasValue, "(obtido " + fps + ")");
    }

    static void TrocaDeAlvoLimpaDados()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        Alimentar(c, "Jogo.exe", 1234, "0x1", 60, 0, 1000);
        c.DefinirAlvo(5678);
        int? fps = c.Fps(1000);
        Checar("trocar de janela nao reaproveita FPS antigo", !fps.HasValue, "(obtido " + fps + ")");
    }

    static void LinhasQuebradasNaoDerrubam()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha("lixo antes do cabecalho", 0);
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        c.ProcessarLinha("", 1);
        c.ProcessarLinha("Jogo.exe,abc,0x1,DXGI,0,0,10.0", 2);
        c.ProcessarLinha("Jogo.exe,1234,0x1,DXGI,0,0,NA,1,1", 3);
        c.ProcessarLinha("Jogo.exe,1234", 4);
        c.ProcessarLinha(null, 5);
        Alimentar(c, "Jogo.exe", 1234, "0x1", 75, 100, 2000);
        int? fps = c.Fps(2100);
        Checar("linhas quebradas sao ignoradas", fps.HasValue && Math.Abs(fps.Value - 75) <= 1, "(obtido " + fps + ")");
    }

    static void CabecalhoSemColunasEsperadas()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha("Application,ProcessID,Outra", 0);
        c.DefinirAlvo(1234);
        c.ProcessarLinha("Jogo.exe,1234,x", 1);
        Checar("cabecalho inesperado gera erro legivel", c.ErroCabecalho != null && !c.Fps(1).HasValue, "");
    }

    static void ReinicioDoPresentMonZeraTempo()
    {
        var c = new CalculadoraFps();
        c.ProcessarLinha(Cabecalho, 0);
        c.DefinirAlvo(1234);
        Alimentar(c, "Jogo.exe", 1234, "0x1", 60, 0, 50000);
        // PresentMon reiniciado: novo cabeçalho e CPUStartTime volta a contar do zero.
        c.ProcessarLinha(Cabecalho, 50000);
        double passo = 1000.0 / 100;
        for (double t = 0; t < 2000; t += passo)
            c.ProcessarLinha(Linha("Jogo.exe", 1234, "0x1", t), 50000 + t);
        int? fps = c.Fps(52000);
        Checar("reinicio do PresentMon nao congela o FPS", fps.HasValue && Math.Abs(fps.Value - 100) <= 1, "(obtido " + fps + ")");
    }
}
