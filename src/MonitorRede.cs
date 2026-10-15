using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace Overlay
{
    // Sessão ETW própria no provedor Microsoft-Windows-Kernel-Network: o Windows avisa "processo X enviou N bytes UDP para IP:porta".
    // Não toca no processo do jogo (nem handle, nem memória). Exige administrador, como o PresentMon.
    // Qualquer falha aqui só pausa a detecção (com nova tentativa): o ping continua no alvo de reserva.
    class MonitorRede : IDisposable
    {
        public const string NomeSessaoPadrao = "OverlayRede";
        // Nome próprio por instância (ver LeitorFps): a prova no jogo usa outro nome e não derruba a sessão do overlay.
        readonly string NomeSessao;
        static readonly Guid ProvedorKernelNetwork = new Guid("7dd42a49-5329-4832-8dfd-43d979153a88");
        const ulong KeywordIpv4 = 0x10;
        const ulong KeywordIpv6 = 0x20;
        const ushort EventoUdpv4Envio = 42;
        const ushort EventoUdpv6Envio = 58;

        const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        const byte TRACE_LEVEL_INFORMATION = 4;
        const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        const uint EVENT_FILTER_TYPE_EVENT_ID = 0x80000200;
        const uint ENABLE_TRACE_PARAMETERS_VERSION_2 = 2;
        static readonly ulong TraceInvalido = ulong.MaxValue;

        // Tamanhos/offsets x64 (o build usa /platform:x64; ver Iniciar). Conferidos contra o Windows SDK na revisão da etapa 5.
        const int TamanhoLogFile = 448;
        const int OffsetLogFileLoggerName = 8;
        const int OffsetLogFileProcessTraceMode = 28;
        const int OffsetLogFileEventRecordCallback = 424;
        const int OffsetRecordEventId = 40;
        const int OffsetRecordUserDataLength = 86;
        const int OffsetRecordUserData = 96;

        delegate void CallbackEvento(IntPtr registro);

        [DllImport("advapi32.dll")]
        static extern uint EnableTraceEx2(ulong sessao, ref Guid provedor, uint codigo, byte nivel,
            ulong qualquerKeyword, ulong todasKeywords, uint timeout, IntPtr parametros);
        // EntryPoint explícito: sem ele o CLR liga à OpenTraceA, que lê o nome UTF-16 como "O" e nunca acha a sessão (erro 4201).
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenTraceW")]
        static extern ulong OpenTrace(IntPtr logfile);
        [DllImport("advapi32.dll")]
        static extern uint ProcessTrace(ulong[] handles, uint quantidade, IntPtr inicio, IntPtr fim);
        [DllImport("advapi32.dll")]
        static extern uint CloseTrace(ulong handle);

        readonly DetectorServidor detector;
        readonly Stopwatch relogio;
        readonly CallbackEvento callback; // campo: impede o GC de coletar o delegate usado pelo código nativo
        readonly ManualResetEvent parar = new ManualResetEvent(false);
        Thread thread;
        ulong sessao;
        volatile bool encerrando;

        public MonitorRede(DetectorServidor detector, Stopwatch relogio, string nomeSessao = NomeSessaoPadrao)
        {
            NomeSessao = nomeSessao;
            this.detector = detector;
            this.relogio = relogio;
            callback = AoReceberEvento;
        }

        public bool Iniciar()
        {
            if (IntPtr.Size != 8)
            {
                Log.Mensagem("MonitorRede exige processo 64 bits; deteccao do servidor desligada.");
                return false;
            }
            thread = new Thread(Loop) { IsBackground = true, Name = "MonitorRede" };
            thread.Start();
            return true;
        }

        // Se a sessão cair (ex.: alguém rodou "logman stop OverlayRede"), recria com espera crescente.
        void Loop()
        {
            int esperaMs = 2000;
            while (!encerrando)
            {
                TimeSpan inicio = relogio.Elapsed;
                try
                {
                    if (CriarSessao())
                    {
                        if (encerrando) break; // Dispose rodou durante a criação: o finally abaixo para a sessão
                        Consumir(NomeSessao, callback);
                    }
                }
                catch (Exception ex)
                {
                    Log.Erro(ex);
                }
                SessaoEtw.Parar(NomeSessao);
                if (encerrando) break;

                if (relogio.Elapsed - inicio > TimeSpan.FromMinutes(1)) esperaMs = 2000;
                if (parar.WaitOne(esperaMs)) break;
                esperaMs = Math.Min(esperaMs * 2, 60000);
            }
            SessaoEtw.Parar(NomeSessao);
        }

        bool CriarSessao()
        {
            uint erro = SessaoEtw.Iniciar(NomeSessao, out sessao);
            if (erro == SessaoEtw.ERROR_ALREADY_EXISTS)
            {
                // Sobrou de uma execução que foi finalizada à força: para a antiga e cria de novo.
                SessaoEtw.Parar(NomeSessao);
                erro = SessaoEtw.Iniciar(NomeSessao, out sessao);
            }
            if (erro != 0)
            {
                Log.Mensagem("StartTrace falhou (" + erro + "); deteccao do servidor pausada.");
                return false;
            }

            Guid provedor = ProvedorKernelNetwork;
            uint resultado = HabilitarComFiltro(ref provedor);
            if (resultado != 0)
            {
                // Filtro por ID de evento é só otimização (evita receber TCP); sem ele ignoramos o resto no callback.
                resultado = EnableTraceEx2(sessao, ref provedor, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION,
                    KeywordIpv4 | KeywordIpv6, 0, 0, IntPtr.Zero);
            }
            if (resultado != 0)
            {
                Log.Mensagem("EnableTraceEx2 falhou (" + resultado + "); deteccao do servidor pausada.");
                return false;
            }
            return true;
        }

        uint HabilitarComFiltro(ref Guid provedor)
        {
            // EVENT_FILTER_EVENT_ID { BOOLEAN FilterIn; UCHAR Reserved; USHORT Count; USHORT Events[2]; }
            IntPtr filtro = Marshal.AllocHGlobal(8);
            // EVENT_FILTER_DESCRIPTOR { ULONGLONG Ptr; ULONG Size; ULONG Type; }
            IntPtr descritor = Marshal.AllocHGlobal(16);
            // ENABLE_TRACE_PARAMETERS (x64, 48 bytes)
            IntPtr parametros = Marshal.AllocHGlobal(48);
            try
            {
                Marshal.WriteByte(filtro, 0, 1);
                Marshal.WriteByte(filtro, 1, 0);
                Marshal.WriteInt16(filtro, 2, 2);
                Marshal.WriteInt16(filtro, 4, (short)EventoUdpv4Envio);
                Marshal.WriteInt16(filtro, 6, (short)EventoUdpv6Envio);

                Marshal.WriteInt64(descritor, 0, filtro.ToInt64());
                Marshal.WriteInt32(descritor, 8, 8);
                Marshal.WriteInt32(descritor, 12, unchecked((int)EVENT_FILTER_TYPE_EVENT_ID));

                SessaoEtw.Zerar(parametros, 48);
                Marshal.WriteInt32(parametros, 0, (int)ENABLE_TRACE_PARAMETERS_VERSION_2);
                Marshal.WriteIntPtr(parametros, 32, descritor);
                Marshal.WriteInt32(parametros, 40, 1);

                return EnableTraceEx2(sessao, ref provedor, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION,
                    KeywordIpv4 | KeywordIpv6, 0, 0, parametros);
            }
            finally
            {
                Marshal.FreeHGlobal(parametros);
                Marshal.FreeHGlobal(descritor);
                Marshal.FreeHGlobal(filtro);
            }
        }

        // Abre a sessão pelo nome e bloqueia consumindo eventos até ela parar. Devolve o código do ProcessTrace (ou do OpenTrace).
        static uint Consumir(string nomeSessao, CallbackEvento aoReceber)
        {
            IntPtr logfile = Marshal.AllocHGlobal(TamanhoLogFile);
            IntPtr nome = Marshal.StringToHGlobalUni(nomeSessao);
            ulong consumidor = TraceInvalido;
            try
            {
                SessaoEtw.Zerar(logfile, TamanhoLogFile);
                Marshal.WriteIntPtr(logfile, OffsetLogFileLoggerName, nome);
                Marshal.WriteInt32(logfile, OffsetLogFileProcessTraceMode,
                    unchecked((int)(PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD)));
                Marshal.WriteIntPtr(logfile, OffsetLogFileEventRecordCallback, Marshal.GetFunctionPointerForDelegate(aoReceber));

                consumidor = OpenTrace(logfile);
                if (consumidor == TraceInvalido)
                {
                    int erroAbertura = Marshal.GetLastWin32Error();
                    Log.Mensagem("OpenTrace falhou (" + erroAbertura + ") para a sessao " + nomeSessao + ".");
                    return (uint)erroAbertura;
                }
                uint erro = ProcessTrace(new[] { consumidor }, 1, IntPtr.Zero, IntPtr.Zero);
                if (erro != 0) Log.Mensagem("ProcessTrace da sessao " + nomeSessao + " terminou com erro " + erro + ".");
                return erro;
            }
            finally
            {
                if (consumidor != TraceInvalido) CloseTrace(consumidor);
                Marshal.FreeHGlobal(nome);
                Marshal.FreeHGlobal(logfile);
                GC.KeepAlive(aoReceber);
            }
        }

        // Para testes: tenta consumir uma sessão existente. Sem admin, o esperado é 5 (acesso negado: achou a sessão);
        // 4201 significaria que o nome chegou errado ao Windows (bug do OpenTraceA).
        public static uint TestarAbertura(string nomeSessao)
        {
            return Consumir(nomeSessao, registro => { });
        }

        void AoReceberEvento(IntPtr registro)
        {
            try
            {
                ushort id = (ushort)Marshal.ReadInt16(registro, OffsetRecordEventId);
                int tamanho = (ushort)Marshal.ReadInt16(registro, OffsetRecordUserDataLength);
                IntPtr dados = Marshal.ReadIntPtr(registro, OffsetRecordUserData);
                if (dados == IntPtr.Zero || tamanho < 12) return;

                // Payload: PID (UInt32), size (UInt32), daddr (destino), saddr, dport, sport...
                // Filtra pelo PID antes de alocar qualquer coisa: a sessão vê os pacotes UDP do sistema inteiro.
                int pid = Marshal.ReadInt32(dados, 0);
                if (!detector.EhAlvo(pid)) return;

                IPAddress destino;
                if (id == EventoUdpv4Envio) destino = new IPAddress(LerBytes(dados, 8, 4));
                else if (id == EventoUdpv6Envio && tamanho >= 24) destino = new IPAddress(LerBytes(dados, 8, 16));
                else return;

                detector.RegistrarEnvio(pid, destino, relogio.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                // Exceção não pode escapar para o código nativo do ETW.
                Log.Erro(ex);
            }
        }

        static byte[] LerBytes(IntPtr origem, int deslocamento, int quantidade)
        {
            var bytes = new byte[quantidade];
            Marshal.Copy(new IntPtr(origem.ToInt64() + deslocamento), bytes, 0, quantidade);
            return bytes;
        }

        public void Dispose()
        {
            if (encerrando) return;
            encerrando = true;
            parar.Set();
            // Parar a sessão faz o ProcessTrace retornar na thread consumidora.
            SessaoEtw.Parar(NomeSessao);
            // A thread é de fundo: sem esperar, o processo pode acabar antes dela parar uma sessão criada durante o Dispose.
            if (thread != null) thread.Join(2000);
        }
    }
}
