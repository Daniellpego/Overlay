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
    // O PresentMon lê eventos ETW do Windows: nada é injetado no jogo nem lido da memória dele. Ele abre, sim, um
    // handle só de consulta (PROCESS_QUERY_LIMITED_INFORMATION, o mesmo do Gerenciador de Tarefas) em cada processo
    // que apresenta frames, para saber quando o processo termina. Exceção aceita pelo Daniel (CONTRATO.md).
    class LeitorFps : IDisposable
    {
        public const string NomeSessaoPadrao = "OverlayFps";
        // Nome próprio por instância: a prova no jogo (tests\ProvaJogo.cs) roda junto com o overlay instalado sem derrubar a sessão dele.
        readonly string NomeSessao;

        public LeitorFps(string nomeSessao = NomeSessaoPadrao)
        {
            NomeSessao = nomeSessao;
        }
        // Mesmo arquivo e hash de tools\baixar-presentmon.ps1. Conferido antes de cada execução porque ele roda como admin.
        public const string ArquivoPresentMon = "PresentMon-2.6.0-x64.exe";
        public const string Sha256PresentMon = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";

        readonly CalculadoraFps calculadora = new CalculadoraFps();
        readonly Stopwatch relogio = Stopwatch.StartNew();
        readonly Queue<string> ultimosErros = new Queue<string>();
        Thread thread;
        Process processo;
        IntPtr job = IntPtr.Zero;
        volatile bool encerrando;
        readonly ManualResetEvent parar = new ManualResetEvent(false);

        public EstadoFps Estado { get; private set; }
        volatile string diagnostico = "Aguardando dados do PresentMon";
        public string Diagnostico { get { return diagnostico; } }

        public bool PrecisaAlternativa
        {
            get { return Estado != EstadoFps.SemAdmin && (Estado != EstadoFps.Ok || Bloqueado || (relogio.Elapsed.TotalSeconds >= 5 && !Fps().HasValue)); }
        }

        // true quando o Windows recusou a sessão ETW do PresentMon (mesmo como admin). Volta a false quando os dados voltam.
        volatile bool bloqueado;
        public bool Bloqueado
        {
            get { return bloqueado; }
            private set { bloqueado = value; }
        }

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
                diagnostico = "PresentMon sem administrador";
                return;
            }
            string exe = Path.Combine(Pastas.Programa, "tools\\PresentMon\\" + ArquivoPresentMon);
            if (!File.Exists(exe))
            {
                Estado = EstadoFps.SemPresentMon;
                diagnostico = "PresentMon ausente";
                Log.Mensagem("PresentMon nao encontrado em " + exe + ". Rode tools\\baixar-presentmon.ps1.");
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
            int? valor = calculadora.Fps(relogio.Elapsed.TotalMilliseconds);
            if (valor.HasValue) { Bloqueado = false; diagnostico = "PresentMon com dados recentes"; }
            else if (Estado == EstadoFps.Ok && !Bloqueado && relogio.Elapsed.TotalSeconds >= 5)
                diagnostico = "PresentMon sem dados recentes da janela ativa";
            return valor;
        }

        // Abre o exe negando escrita e exclusão a outros processos e confere o SHA-256. O chamador mantém o arquivo
        // aberto até o PresentMon terminar, para ninguém trocar o exe entre a conferência e a execução.
        static FileStream AbrirConferido(string exe)
        {
            var arquivo = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(arquivo)).Replace("-", "");
                    if (hash == Sha256PresentMon) return arquivo;
                    Log.Mensagem("Hash do PresentMon nao confere (" + hash + "); FPS desligado por seguranca. Rode tools\\baixar-presentmon.ps1.");
                }
            }
            catch (IOException ex)
            {
                Log.Erro(ex);
            }
            arquivo.Dispose();
            return null;
        }

        void Loop(string exe)
        {
            int esperaMs = 2000;
            while (!encerrando)
            {
                TimeSpan inicio = relogio.Elapsed;
                try
                {
                    if (!RodarPresentMon(exe))
                    {
                        // Exe adulterado ou ilegível: tentar de novo não resolve e encheria o log.
                        Estado = EstadoFps.SemPresentMon;
                        diagnostico = "PresentMon recusado: arquivo ausente, ilegível ou hash inválido";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    diagnostico = "PresentMon falhou: " + ex.Message;
                    Log.Erro(ex);
                }
                if (encerrando) break;

                // Se rodou bem por um bom tempo, a queda foi pontual: volta a tentar rápido.
                if (relogio.Elapsed - inicio > TimeSpan.FromMinutes(1)) esperaMs = 2000;
                if (parar.WaitOne(esperaMs)) break;
                esperaMs = Math.Min(esperaMs * 2, 60000);
            }
        }

        // false = não adianta tentar de novo (hash não confere).
        bool RodarPresentMon(string exe)
        {
            using (FileStream conferido = AbrirConferido(exe))
            {
                if (conferido == null) return false;
                RodarPresentMonConferido(exe);
                return true;
            }
        }

        void RodarPresentMonConferido(string exe)
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
                    // Dispose rodou entre o while e o Start: não deixar PresentMon nem a sessão ETW dele órfãos.
                    SessaoEtw.Parar(NomeSessao);
                    if (!p.WaitForExit(2000)) p.Kill();
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
                    {
                        string erros = string.Join(" | ", ultimosErros.ToArray());
                        // "access denied" mesmo como admin = o Windows está recusando sessões ETW (com o FC 27 aberto,
                        // o anticheat da EA faz isso; ver CONTRATO.md). O overlay mostra BLQ e segue tentando.
                        Bloqueado = p.ExitCode == 5 || erros.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            erros.IndexOf("acesso negado", StringComparison.OrdinalIgnoreCase) >= 0;
                        diagnostico = Bloqueado ? "PresentMon bloqueado pelo Windows" : "PresentMon saiu com código " + p.ExitCode;
                        Log.Mensagem("PresentMon saiu com codigo " + p.ExitCode + ": " + erros);
                        ultimosErros.Clear();
                    }
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
            if (encerrando) return;
            encerrando = true;
            parar.Set();
            Process p = processo;
            if (p != null)
            {
                // Parar a sessão pelo nome faz o PresentMon sair sozinho e desligar os provedores ETW.
                // Kill direto deixaria a sessão "OverlayFps" ligada, sem consumidor, até o próximo boot.
                try
                {
                    SessaoEtw.Parar(NomeSessao);
                    if (!p.WaitForExit(2000)) p.Kill();
                }
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
