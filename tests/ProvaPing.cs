using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Overlay;

// Prova do ping: o MedidorPing (mesmo código do overlay) contra o ping.exe do Windows, nos mesmos alvos e ao mesmo
// tempo, por 30 s. Compara as medianas. Critério: diferença <= 2 ms ou 10% (o que for maior).
static class ProvaPing
{
    static int Main()
    {
        string[] alvos = { "1.1.1.1", "8.8.8.8", "208.67.222.222" };
        const int segundos = 30;
        int falhas = 0;

        var medidores = new List<MedidorPing>();
        var leituras = new List<List<int>>();
        var pingsWindows = new List<Process>();
        foreach (string alvo in alvos)
        {
            var m = new MedidorPing(alvo, null);
            m.Iniciar();
            medidores.Add(m);
            leituras.Add(new List<int>());
            var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n " + segundos + " " + alvo)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            pingsWindows.Add(Process.Start(info));
        }

        // Lê o valor que o overlay mostraria, uma vez por segundo (depois de 3 s de aquecimento).
        Thread.Sleep(3000);
        for (int s = 0; s < segundos - 3; s++)
        {
            for (int i = 0; i < alvos.Length; i++)
            {
                int? ms;
                OrigemPing origem;
                medidores[i].Ler(out ms, out origem);
                if (ms.HasValue) leituras[i].Add(ms.Value);
            }
            Thread.Sleep(1000);
        }

        Console.WriteLine("alvo            | overlay (mediana, min-max) | ping.exe (mediana, min-max) | diferenca");
        for (int i = 0; i < alvos.Length; i++)
        {
            string saida = pingsWindows[i].StandardOutput.ReadToEnd();
            pingsWindows[i].WaitForExit();
            medidores[i].Dispose();
            var windows = new List<int>();
            foreach (Match m in Regex.Matches(saida, @"(?:tempo|time)[=<](\d+)ms")) windows.Add(int.Parse(m.Groups[1].Value));

            if (leituras[i].Count == 0 || windows.Count == 0)
            {
                Console.WriteLine("{0,-15} | sem resposta (overlay {1} leituras, ping.exe {2})", alvos[i], leituras[i].Count, windows.Count);
                falhas++;
                continue;
            }
            int medOverlay = Mediana(leituras[i]), medWindows = Mediana(windows);
            int diferenca = medOverlay - medWindows;
            bool ok = Math.Abs(diferenca) <= Math.Max(2, medWindows * 0.10);
            if (!ok) falhas++;
            Console.WriteLine("{0,-15} | {1,4} ms ({2}-{3}) {4,12} | {5,4} ms ({6}-{7}) {8,14} | {9:+0;-0;0} ms {10}",
                alvos[i], medOverlay, Min(leituras[i]), Max(leituras[i]), "", medWindows, Min(windows), Max(windows), "", diferenca, ok ? "OK" : "FALHA");
        }
        Console.WriteLine(falhas == 0 ? "Prova do ping: tudo OK" : "Prova do ping: " + falhas + " falha(s)");
        return falhas == 0 ? 0 : 1;
    }

    static int Mediana(List<int> valores)
    {
        var copia = new List<int>(valores);
        copia.Sort();
        return copia[copia.Count / 2];
    }

    static int Min(List<int> v) { int m = int.MaxValue; foreach (int x in v) m = Math.Min(m, x); return m; }
    static int Max(List<int> v) { int m = int.MinValue; foreach (int x in v) m = Math.Max(m, x); return m; }
}
