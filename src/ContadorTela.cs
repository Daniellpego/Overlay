using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Overlay
{
    // Plano B do FPS: conta quantas vezes a imagem do monitor muda por segundo, pela API Desktop Duplication do Windows
    // (a mesma do OBS e do compartilhamento de tela do Discord). Não toca no processo do jogo, não usa ETW e não precisa
    // de admin; serve quando o anticheat (ex.: EA Javelin no FC 27) faz o Windows recusar as sessões ETW do PresentMon.
    // Limites: mede o que chega ao monitor (teto = taxa de atualização da tela) e conta qualquer mudança na tela inteira,
    // então só é confiável com o jogo em tela cheia nesse monitor.
    // Chamadas COM feitas pela vtable (índices do SDK do Windows; offsets x64, build /platform:x64).
    class ContadorTela : IDisposable
    {
        const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
        const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
        const int D3D_DRIVER_TYPE_HARDWARE = 1;
        const uint D3D11_SDK_VERSION = 7;
        const double JanelaMs = 1000;
        const double ValidadeMs = 1500;

        // Índices na vtable (IUnknown 0–2, IDXGIObject 3–6).
        const int VtQueryInterface = 0;
        const int VtRelease = 2;
        const int VtDeviceGetAdapter = 7;
        const int VtAdapterEnumOutputs = 7;
        const int VtOutputGetDesc = 7;
        const int VtOutput1DuplicateOutput = 22;
        const int VtDuplicationAcquireNextFrame = 8;
        const int VtDuplicationReleaseFrame = 14;

        static readonly Guid IidDxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        // IID conferido no dxgi1_2.idl (Wine), que espelha o dxgi1_2.h do SDK.
        static readonly Guid IidDxgiOutput1 = new Guid("00cddea8-939b-4b83-a340-a685226666cc");

        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adaptador, int tipo, IntPtr software, uint flags, IntPtr niveis,
            uint quantidadeNiveis, uint versaoSdk, out IntPtr dispositivo, out int nivel, out IntPtr contexto);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long frequencia);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr janela, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnQueryInterface(IntPtr obj, ref Guid iid, out IntPtr saida);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint FnRelease(IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnGetAdapter(IntPtr obj, out IntPtr adaptador);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnEnumOutputs(IntPtr obj, uint indice, out IntPtr saida);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnGetDesc(IntPtr obj, IntPtr desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnDuplicateOutput(IntPtr obj, IntPtr dispositivo, out IntPtr duplicacao);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnAcquireNextFrame(IntPtr obj, uint timeoutMs, IntPtr info, out IntPtr recurso);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnReleaseFrame(IntPtr obj);

        readonly object trava = new object();
        readonly Queue<KeyValuePair<long, int>> quadros = new Queue<KeyValuePair<long, int>>();
        readonly ManualResetEvent parar = new ManualResetEvent(false);
        readonly long frequenciaQpc;
        Thread thread;
        volatile bool encerrando;
        long ultimoQuadroQpc;

        public string UltimoErro { get; private set; }

        readonly Func<IntPtr> janelaAlvo;

        // janelaAlvo: janela cujo monitor será medido (padrão: a janela em primeiro plano, que no jogo é o próprio jogo).
        public ContadorTela(Func<IntPtr> janelaAlvo = null)
        {
            QueryPerformanceFrequency(out frequenciaQpc);
            this.janelaAlvo = janelaAlvo ?? GetForegroundWindow;
        }

        public void Iniciar()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "ContadorTela" };
            thread.Start();
        }

        // Quadros por segundo no monitor do jogo, ou null sem dados recentes.
        // Desconta as atualizações do próprio overlay (ver RegistrarAtualizacaoPropria): sem isso o FPS sairia ~+2 com o jogo
        // parado ou abaixo da taxa do monitor (prova em tests\ProvaFpsTela.cs).
        public int? Fps()
        {
            long agora = Qpc();
            lock (trava)
            {
                if (quadros.Count == 0 || MsEntre(ultimoQuadroQpc, agora) > ValidadeMs) return null;
                Podar(agora);
                int total = 0;
                foreach (KeyValuePair<long, int> q in quadros) total += q.Value;
                while (proprias.Count > 0 && MsEntre(proprias.Peek(), agora) > JanelaMs) proprias.Dequeue();
                return Math.Max(0, total - proprias.Count);
            }
        }

        readonly Queue<long> proprias = new Queue<long>();

        // O overlay chama a cada redesenho; só conta se ele estiver no monitor medido.
        public void RegistrarAtualizacaoPropria(IntPtr janelaOverlay)
        {
            if (MonitorFromWindow(janelaOverlay, 2) != monitorMedido) return;
            lock (trava) proprias.Enqueue(Qpc());
        }

        volatile IntPtr monitorMedido;

        void Loop()
        {
            int esperaMs = 1000;
            while (!encerrando)
            {
                try
                {
                    Duplicar();
                    esperaMs = 1000;
                }
                catch (Exception ex)
                {
                    UltimoErro = ex.Message;
                    Log.Mensagem("ContadorTela: " + ex.Message);
                }
                if (parar.WaitOne(esperaMs)) break;
                esperaMs = Math.Min(esperaMs * 2, 30000);
            }
        }

        // Cria a duplicação do monitor onde está a janela da frente e conta quadros até perder o acesso
        // (troca de resolução, tela de UAC, jogo em tela cheia exclusiva) ou o monitor do jogo mudar.
        void Duplicar()
        {
            IntPtr dispositivo = IntPtr.Zero, contexto = IntPtr.Zero, dxgiDevice = IntPtr.Zero, adaptador = IntPtr.Zero;
            IntPtr saida = IntPtr.Zero, saida1 = IntPtr.Zero, duplicacao = IntPtr.Zero;
            IntPtr info = Marshal.AllocHGlobal(48);   // DXGI_OUTDUPL_FRAME_INFO
            IntPtr desc = Marshal.AllocHGlobal(96);   // DXGI_OUTPUT_DESC
            try
            {
                int nivel;
                Checar(D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, 0, IntPtr.Zero, 0,
                    D3D11_SDK_VERSION, out dispositivo, out nivel, out contexto), "D3D11CreateDevice");
                Guid iid = IidDxgiDevice;
                Checar(Metodo<FnQueryInterface>(dispositivo, VtQueryInterface)(dispositivo, ref iid, out dxgiDevice), "IDXGIDevice");
                Checar(Metodo<FnGetAdapter>(dxgiDevice, VtDeviceGetAdapter)(dxgiDevice, out adaptador), "GetAdapter");

                IntPtr monitorAlvo = MonitorFromWindow(janelaAlvo(), 2 /* MONITOR_DEFAULTTONEAREST */);
                IntPtr monitorDuplicado = IntPtr.Zero;
                for (uint i = 0; ; i++)
                {
                    IntPtr candidata;
                    if (Metodo<FnEnumOutputs>(adaptador, VtAdapterEnumOutputs)(adaptador, i, out candidata) != 0) break;
                    Checar(Metodo<FnGetDesc>(candidata, VtOutputGetDesc)(candidata, desc), "GetDesc");
                    IntPtr monitor = Marshal.ReadIntPtr(desc, 88); // DXGI_OUTPUT_DESC.Monitor
                    // Fica com o monitor do jogo; se não achar, com o primeiro da placa.
                    if (saida == IntPtr.Zero || monitor == monitorAlvo)
                    {
                        Liberar(ref saida);
                        saida = candidata;
                        monitorDuplicado = monitor;
                        if (monitor == monitorAlvo) break;
                    }
                    else Liberar(ref candidata);
                }
                if (saida == IntPtr.Zero) throw new InvalidOperationException("nenhum monitor ligado a placa de video principal");

                iid = IidDxgiOutput1;
                Checar(Metodo<FnQueryInterface>(saida, VtQueryInterface)(saida, ref iid, out saida1), "IDXGIOutput1");
                Checar(Metodo<FnDuplicateOutput>(saida1, VtOutput1DuplicateOutput)(saida1, dispositivo, out duplicacao), "DuplicateOutput");
                UltimoErro = null;
                monitorMedido = monitorDuplicado;

                FnAcquireNextFrame adquirir = Metodo<FnAcquireNextFrame>(duplicacao, VtDuplicationAcquireNextFrame);
                FnReleaseFrame soltar = Metodo<FnReleaseFrame>(duplicacao, VtDuplicationReleaseFrame);
                long proximaChecagemMonitor = Qpc() + frequenciaQpc;
                while (!encerrando)
                {
                    IntPtr recurso;
                    int hr = adquirir(duplicacao, 250, info, out recurso);
                    if (hr == DXGI_ERROR_WAIT_TIMEOUT) { }
                    else if (hr == DXGI_ERROR_ACCESS_LOST) return;
                    else
                    {
                        Checar(hr, "AcquireNextFrame");
                        long ultimoPresent = Marshal.ReadInt64(info, 0);
                        int acumulados = Marshal.ReadInt32(info, 16);
                        // LastPresentTime = 0 é só o ponteiro do mouse se mexendo: não é quadro novo.
                        if (ultimoPresent != 0 && acumulados > 0) Registrar(ultimoPresent, acumulados);
                        if (recurso != IntPtr.Zero) Liberar(ref recurso);
                        soltar(duplicacao);
                    }

                    // Jogo foi para outro monitor: recria a duplicação no monitor certo.
                    long agora = Qpc();
                    if (agora >= proximaChecagemMonitor)
                    {
                        proximaChecagemMonitor = agora + frequenciaQpc;
                        if (MonitorFromWindow(janelaAlvo(), 2) != monitorDuplicado) return;
                    }
                }
            }
            finally
            {
                Liberar(ref duplicacao);
                Liberar(ref saida1);
                Liberar(ref saida);
                Liberar(ref adaptador);
                Liberar(ref dxgiDevice);
                Liberar(ref contexto);
                Liberar(ref dispositivo);
                Marshal.FreeHGlobal(desc);
                Marshal.FreeHGlobal(info);
            }
        }

        void Registrar(long qpc, int acumulados)
        {
            lock (trava)
            {
                quadros.Enqueue(new KeyValuePair<long, int>(qpc, acumulados));
                ultimoQuadroQpc = Math.Max(ultimoQuadroQpc, qpc);
                Podar(Qpc());
            }
        }

        void Podar(long agora)
        {
            while (quadros.Count > 0 && MsEntre(quadros.Peek().Key, agora) > JanelaMs) quadros.Dequeue();
        }

        double MsEntre(long de, long ate)
        {
            return (ate - de) * 1000.0 / frequenciaQpc;
        }

        static long Qpc()
        {
            return System.Diagnostics.Stopwatch.GetTimestamp();
        }

        static T Metodo<T>(IntPtr objeto, int indice) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(objeto);
            IntPtr funcao = Marshal.ReadIntPtr(vtable, indice * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(funcao, typeof(T)) as T;
        }

        static void Liberar(ref IntPtr objeto)
        {
            if (objeto == IntPtr.Zero) return;
            Metodo<FnRelease>(objeto, VtRelease)(objeto);
            objeto = IntPtr.Zero;
        }

        static void Checar(int hr, string chamada)
        {
            if (hr < 0) throw new InvalidOperationException(chamada + " falhou (0x" + hr.ToString("X8") + ")");
        }

        public void Dispose()
        {
            if (encerrando) return;
            encerrando = true;
            parar.Set();
            if (thread != null) thread.Join(1000);
        }
    }
}
