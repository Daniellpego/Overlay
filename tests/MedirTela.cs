using System;
using System.Diagnostics;
using System.Threading;
using Overlay;

// Ferramenta de medição do ContadorTela: imprime o FPS da tela a cada segundo por N segundos (padrão 10)
// e o uso de CPU do próprio processo. Uso: MedirTela.exe [segundos]
static class MedirTela
{
    static int Main(string[] args)
    {
        int segundos = args.Length > 0 ? int.Parse(args[0]) : 10;
        // Segundo argumento opcional: nome do processo cujo monitor medir (ex.: FC27). Sem ele, o monitor da janela da frente.
        Func<IntPtr> alvo = null;
        if (args.Length > 1)
        {
            string nome = args[1];
            alvo = () =>
            {
                Process[] p = Process.GetProcessesByName(nome);
                return p.Length > 0 ? p[0].MainWindowHandle : IntPtr.Zero;
            };
            Console.WriteLine("Medindo o monitor da janela de " + nome);
        }
        using (var contador = new ContadorTela(alvo))
        {
            contador.Iniciar();
            Process eu = Process.GetCurrentProcess();
            TimeSpan cpu0 = eu.TotalProcessorTime;
            var relogio = Stopwatch.StartNew();
            for (int i = 1; i <= segundos; i++)
            {
                Thread.Sleep(1000);
                int? fps = contador.Fps();
                Console.WriteLine("{0,2}s  FPS da tela: {1}{2}", i, fps.HasValue ? fps.Value.ToString() : "---",
                    contador.UltimoErro != null ? "  (erro: " + contador.UltimoErro + ")" : "");
            }
            eu.Refresh();
            double cpu = (eu.TotalProcessorTime - cpu0).TotalMilliseconds / relogio.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            Console.WriteLine("CPU do medidor: {0:N3}%  RAM: {1:N1} MB", cpu, eu.WorkingSet64 / 1048576.0);
        }
        return 0;
    }
}
