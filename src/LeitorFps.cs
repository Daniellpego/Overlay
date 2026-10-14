using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;

namespace Overlay
{
    enum EstadoFps { Ok, SemAdmin, SemPresentMon }

    // Roda o PresentMon (Intel) em segundo plano e alimenta a CalculadoraFps com o CSV que ele escreve no stdout.
    // O PresentMon lê eventos ETW do Windows: nada é injetado no jogo e nenhum handle do jogo é aberto.
    class LeitorFps : IDisposable
    {
        const string NomeSessao = "OverlayFps";

        readonly CalculadoraFps calculadora = new CalculadoraFps();
        readonly Stopwatch relogio = Stopwatch.StartNew();
        readonly Queue<string> ultimosErros = new Queue<string>();
        Thread thread;
        Process processo;
        IntPtr job = IntPtr.Zero;
        volatile bool encerrando;

        public EstadoFps Estado { get; private set; }

        public static bool EhAdministrador()
        {
            using (WindowsIdentity identidade = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identidade).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public void Iniciar()
        {
            if (!EhAdministrador())
            {
                Estado = EstadoFps.SemAdmin;
                return;
            }
            string exe = LocalizarPresentMon();
            if (exe == null)
            {
                Estado = EstadoFps.SemPresentMon;
                Log.Mensagem("PresentMon nao encontrado em tools\\PresentMon. Rode tools\\baixar-presentmon.ps1.");
                return;
            }

            Estado = EstadoFps.Ok;
            // Job com "matar ao fechar": se o overlay cair ou for finalizado, o Windows encerra o PresentMon junto.
            job = Native.CriarJobQueEncerraFilhos();
            thread = new Thread(() => Loop(exe)) { IsBackground = true, Name = "PresentMon" };
            thread.Start();
        }

        public void DefinirProcessoAlvo(int pid)
        {
            calculadora.DefinirAlvo(pid);
        }

        public int? Fps()
        {
            return calculadora.Fps(relogio.Elapsed.TotalMilliseconds);
        }

        static string LocalizarPresentMon()
        {
            string pasta = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools\\PresentMon");
            if (!Directory.Exists(pasta)) return null;
            string[] candidatos = Directory.GetFiles(pasta, "PresentMon-*-x64.exe");
            if (candidatos.Length == 0) return null;
            Array.Sort(candidatos, StringComparer.OrdinalIgnoreCase);
            return candidatos[candidatos.Length - 1];
        }

        void Loop(string exe)
        {
            int esperaMs = 2000;
            while (!encerrando)
            {
                TimeSpan inicio = relogio.Elapsed;
                try
                {
                    RodarPresentMon(exe);
                }
                catch (Exception ex)
                {
                    Log.Erro(ex);
                }
                if (encerrando) break;

                // Se rodou bem por um bom tempo, a queda foi pontual: volta a tentar rápido.
                if (relogio.Elapsed - inicio > TimeSpan.FromMinutes(1)) esperaMs = 2000;
                Thread.Sleep(esperaMs);
                esperaMs = Math.Min(esperaMs * 2, 60000);
            }
        }

        void RodarPresentMon(string exe)
        {
            var info = new ProcessStartInfo(exe,
                "--output_stdout --no_console_stats --session_name " + NomeSessao + " --stop_existing_session " +
                "--no_track_display --no_track_gpu --no_track_input")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var p = new Process { StartInfo = info })
            {
                p.ErrorDataReceived += (s, e) => GuardarErro(e.Data);
                p.Start();
                processo = p;
                if (job != IntPtr.Zero) Native.ColocarNoJob(job, p.Handle);
                if (encerrando)
                {
                    // Dispose rodou entre o while e o Start: não deixar PresentMon órfão.
                    p.Kill();
                    return;
                }
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch (InvalidOperationException) { }
                p.BeginErrorReadLine();

                string linha;
                bool cabecalhoConferido = false;
                while ((linha = p.StandardOutput.ReadLine()) != null)
                {
                    calculadora.ProcessarLinha(linha, relogio.Elapsed.TotalMilliseconds);
                    if (!cabecalhoConferido && calculadora.ErroCabecalho != null)
                    {
                        Log.Mensagem(calculadora.ErroCabecalho);
                        cabecalhoConferido = true;
                    }
                }

                p.WaitForExit();
                processo = null;
                if (!encerrando)
                {
                    lock (ultimosErros)
                        Log.Mensagem("PresentMon saiu com codigo " + p.ExitCode + ": " + string.Join(" | ", ultimosErros.ToArray()));
                }
            }
        }

        void GuardarErro(string linha)
        {
            if (string.IsNullOrEmpty(linha)) return;
            lock (ultimosErros)
            {
                ultimosErros.Enqueue(linha.Trim());
                while (ultimosErros.Count > 6) ultimosErros.Dequeue();
            }
        }

        public void Dispose()
        {
            encerrando = true;
            Process p = processo;
            if (p != null)
            {
                try { p.Kill(); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (job != IntPtr.Zero)
            {
                Native.FecharHandle(job);
                job = IntPtr.Zero;
            }
        }
    }
}
