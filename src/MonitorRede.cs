using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace Overlay
{
    // Sessão ETW própria no provedor Microsoft-Windows-Kernel-Network: o Windows avisa "processo X enviou N bytes UDP para IP:porta".
    // Não toca no processo do jogo (nem handle, nem memória). Exige administrador, como o PresentMon.
    // Qualquer falha aqui só desliga a detecção: o ping continua no alvo de reserva.
    class MonitorRede : IDisposable
    {
        const string NomeSessao = "OverlayRede";
        static readonly Guid ProvedorKernelNetwork = new Guid("7dd42a49-5329-4832-8dfd-43d979153a88");
        const ulong KeywordIpv4 = 0x10;
        const ulong KeywordIpv6 = 0x20;
        const ushort EventoUdpv4Envio = 42;
        const ushort EventoUdpv6Envio = 58;

        const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        const uint EVENT_TRACE_CONTROL_STOP = 1;
        const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        const byte TRACE_LEVEL_INFORMATION = 4;
        const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        const uint EVENT_FILTER_TYPE_EVENT_ID = 0x80000200;
        const uint ENABLE_TRACE_PARAMETERS_VERSION_2 = 2;
        const uint ERROR_ALREADY_EXISTS = 183;
        static readonly ulong TraceInvalido = ulong.MaxValue;

        // Tamanhos/offsets x64 (o build usa /platform:x64; ver Iniciar).
        const int TamanhoPropriedades = 120;
        const int OffsetLoggerNameOffset = 116;
        const int TamanhoLogFile = 448;
        const int OffsetLogFileLoggerName = 8;
        const int OffsetLogFileProcessTraceMode = 28;
        const int OffsetLogFileEventRecordCallback = 424;
        const int OffsetRecordEventId = 40;
        const int OffsetRecordUserDataLength = 86;
        const int OffsetRecordUserData = 96;

        delegate void CallbackEvento(IntPtr registro);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern uint StartTrace(out ulong sessao, string nome, IntPtr propriedades);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern uint ControlTrace(ulong sessao, string nome, IntPtr propriedades, uint codigo);
        [DllImport("advapi32.dll")]
        static extern uint EnableTraceEx2(ulong sessao, ref Guid provedor, uint codigo, byte nivel,
            ulong qualquerKeyword, ulong todasKeywords, uint timeout, IntPtr parametros);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern ulong OpenTrace(IntPtr logfile);
        [DllImport("advapi32.dll")]
        static extern uint ProcessTrace(ulong[] handles, uint quantidade, IntPtr inicio, IntPtr fim);
        [DllImport("advapi32.dll")]
        static extern uint CloseTrace(ulong handle);

        readonly DetectorServidor detector;
        readonly Stopwatch relogio;
        readonly CallbackEvento callback; // campo: impede o GC de coletar o delegate usado pelo código nativo
        Thread thread;
        ulong sessao;
        ulong consumidor = ulong.MaxValue;
        volatile bool encerrando;

        public MonitorRede(DetectorServidor detector, Stopwatch relogio)
        {
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
            try
            {
                if (!CriarSessao()) return false;
                thread = new Thread(Consumir) { IsBackground = true, Name = "MonitorRede" };
                thread.Start();
                return true;
            }
            catch (Exception ex)
            {
                Log.Erro(ex);
                PararSessao();
                return false;
            }
        }

        bool CriarSessao()
        {
            IntPtr propriedades = NovasPropriedades();
            try
            {
                uint erro = StartTrace(out sessao, NomeSessao, propriedades);
                if (erro == ERROR_ALREADY_EXISTS)
                {
                    // Sobrou de uma execução que foi finalizada à força: para a antiga e cria de novo.
                    PararSessao();
                    Marshal.FreeHGlobal(propriedades);
                    propriedades = NovasPropriedades();
                    erro = StartTrace(out sessao, NomeSessao, propriedades);
                }
                if (erro != 0)
                {
                    Log.Mensagem("StartTrace falhou (" + erro + "); deteccao do servidor desligada.");
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(propriedades);
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
                Log.Mensagem("EnableTraceEx2 falhou (" + resultado + "); deteccao do servidor desligada.");
                PararSessao();
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

                Zerar(parametros, 48);
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

        static IntPtr NovasPropriedades()
        {
            int tamanhoNome = (NomeSessao.Length + 1) * 2;
            int total = TamanhoPropriedades + tamanhoNome;
            IntPtr p = Marshal.AllocHGlobal(total);
            Zerar(p, total);
            Marshal.WriteInt32(p, 0, total);                         // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);                            // Wnode.ClientContext = QPC
            Marshal.WriteInt32(p, 44, (int)WNODE_FLAG_TRACED_GUID);  // Wnode.Flags
            Marshal.WriteInt32(p, 48, 64);                           // BufferSize (KB)
            Marshal.WriteInt32(p, 52, 2);                            // MinimumBuffers
            Marshal.WriteInt32(p, 56, 8);                            // MaximumBuffers
            Marshal.WriteInt32(p, 64, (int)EVENT_TRACE_REAL_TIME_MODE); // LogFileMode
            Marshal.WriteInt32(p, 68, 1);                            // FlushTimer (s)
            Marshal.WriteInt32(p, OffsetLoggerNameOffset, TamanhoPropriedades);
            return p;
        }

        static void Zerar(IntPtr p, int tamanho)
        {
            for (int i = 0; i < tamanho; i++) Marshal.WriteByte(p, i, 0);
        }

        static void PararSessao()
        {
            IntPtr propriedades = NovasPropriedades();
            try { ControlTrace(0, NomeSessao, propriedades, EVENT_TRACE_CONTROL_STOP); }
            finally { Marshal.FreeHGlobal(propriedades); }
        }

        void Consumir()
        {
            IntPtr logfile = Marshal.AllocHGlobal(TamanhoLogFile);
            IntPtr nome = Marshal.StringToHGlobalUni(NomeSessao);
            try
            {
                Zerar(logfile, TamanhoLogFile);
                Marshal.WriteIntPtr(logfile, OffsetLogFileLoggerName, nome);
                Marshal.WriteInt32(logfile, OffsetLogFileProcessTraceMode,
                    unchecked((int)(PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD)));
                Marshal.WriteIntPtr(logfile, OffsetLogFileEventRecordCallback, Marshal.GetFunctionPointerForDelegate(callback));

                consumidor = OpenTrace(logfile);
                if (consumidor == TraceInvalido)
                {
                    Log.Mensagem("OpenTrace falhou (" + Marshal.GetLastWin32Error() + "); deteccao do servidor desligada.");
                    return;
                }
                // Bloqueia até a sessão ser parada no Dispose.
                uint erro = ProcessTrace(new[] { consumidor }, 1, IntPtr.Zero, IntPtr.Zero);
                if (erro != 0 && !encerrando) Log.Mensagem("ProcessTrace terminou com erro " + erro + ".");
            }
            catch (Exception ex)
            {
                Log.Erro(ex);
            }
            finally
            {
                if (consumidor != TraceInvalido) CloseTrace(consumidor);
                Marshal.FreeHGlobal(nome);
                Marshal.FreeHGlobal(logfile);
            }
        }

        void AoReceberEvento(IntPtr registro)
        {
            try
            {
                ushort id = (ushort)Marshal.ReadInt16(registro, OffsetRecordEventId);
                int tamanho = (ushort)Marshal.ReadInt16(registro, OffsetRecordUserDataLength);
                IntPtr dados = Marshal.ReadIntPtr(registro, OffsetRecordUserData);
                if (dados == IntPtr.Zero) return;

                // Payload: PID (UInt32), size (UInt32), daddr (destino), saddr, dport, sport...
                IPAddress destino;
                if (id == EventoUdpv4Envio && tamanho >= 12)
                {
                    destino = new IPAddress(LerBytes(dados, 8, 4));
                }
                else if (id == EventoUdpv6Envio && tamanho >= 24)
                {
                    destino = new IPAddress(LerBytes(dados, 8, 16));
                }
                else return;

                int pid = Marshal.ReadInt32(dados, 0);
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
            // Parar a sessão faz o ProcessTrace retornar na thread consumidora.
            PararSessao();
        }
    }
}
