using System;
using System.Diagnostics;
using System.Globalization;
using Overlay;

// Ensaio isolado e reproduzivel. Nao abre janela, ETW ou jogo; compara a mesma entrada entre revisoes.
static class MedirParserFps
{
    static int Main()
    {
        AppDomain.MonitoringIsEnabled = true;
        const string cabecalho = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,CPUStartTime,MsCPUBusy,MsCPUWait";
        string[] linhas = new string[1000];
        for (int i = 0; i < linhas.Length; i++)
            linhas[i] = "Exemplo.exe," + (i % 10 == 0 ? "1234" : "5678") + ",0x1,DXGI,0,0," +
                (i * 1.666667).ToString("F4", CultureInfo.InvariantCulture) + ",1.0000,1.0000";
        var c = new CalculadoraFps();
        c.DefinirAlvo(1234);
        for (int i = 0; i < 5; i++) Processar(c, cabecalho, linhas);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long antes = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
        int coletas = GC.CollectionCount(0);
        var tempo = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) Processar(c, cabecalho, linhas);
        tempo.Stop();
        GC.Collect(); // Atualiza o contador de alocacoes tambem quando o candidato quase nao coleta.
        long bytes = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - antes;
        Console.WriteLine("linhas=200000;outros_processos=90%;alocado_bytes={0};gen0={1};tempo_ms={2};fps={3}",
            bytes, GC.CollectionCount(0) - coletas, tempo.ElapsedMilliseconds, c.Fps(1666));
        return c.Fps(1666) == 60 ? 0 : 1;
    }

    static void Processar(CalculadoraFps c, string cabecalho, string[] linhas)
    {
        c.ProcessarLinha(cabecalho, 0);
        for (int i = 0; i < linhas.Length; i++) c.ProcessarLinha(linhas[i], i * 1.666667);
    }
}
