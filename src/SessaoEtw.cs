using System;
using System.Runtime.InteropServices;

namespace Overlay
{
    // Criar e parar sessões ETW real-time pelo nome. Usado pelo MonitorRede e para encerrar a sessão do PresentMon.
    // Offsets de EVENT_TRACE_PROPERTIES para x64 (o build usa /platform:x64).
    static class SessaoEtw
    {
        const int TamanhoPropriedades = 120;
        const int OffsetLoggerNameOffset = 116;
        const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        const uint EVENT_TRACE_CONTROL_STOP = 1;
        public const uint ERROR_ALREADY_EXISTS = 183;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartTraceW")]
        static extern uint StartTrace(out ulong sessao, string nome, IntPtr propriedades);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")]
        static extern uint ControlTrace(ulong sessao, string nome, IntPtr propriedades, uint codigo);

        public static uint Iniciar(string nome, out ulong sessao)
        {
            IntPtr propriedades = NovasPropriedades(nome);
            try
            {
                return StartTrace(out sessao, nome, propriedades);
            }
            finally
            {
                Marshal.FreeHGlobal(propriedades);
            }
        }

        // Parar a sessão faz quem estiver consumindo (ProcessTrace, PresentMon) terminar por conta própria.
        public static void Parar(string nome)
        {
            IntPtr propriedades = NovasPropriedades(nome);
            try { ControlTrace(0, nome, propriedades, EVENT_TRACE_CONTROL_STOP); }
            finally { Marshal.FreeHGlobal(propriedades); }
        }

        static IntPtr NovasPropriedades(string nome)
        {
            int total = TamanhoPropriedades + (nome.Length + 1) * 2;
            IntPtr p = Marshal.AllocHGlobal(total);
            Zerar(p, total);
            Marshal.WriteInt32(p, 0, total);                            // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);                               // Wnode.ClientContext = QPC
            Marshal.WriteInt32(p, 44, (int)WNODE_FLAG_TRACED_GUID);     // Wnode.Flags
            Marshal.WriteInt32(p, 48, 64);                              // BufferSize (KB)
            Marshal.WriteInt32(p, 52, 2);                               // MinimumBuffers
            Marshal.WriteInt32(p, 56, 8);                               // MaximumBuffers
            Marshal.WriteInt32(p, 64, (int)EVENT_TRACE_REAL_TIME_MODE); // LogFileMode
            Marshal.WriteInt32(p, 68, 1);                               // FlushTimer (s)
            Marshal.WriteInt32(p, OffsetLoggerNameOffset, TamanhoPropriedades);
            return p;
        }

        public static void Zerar(IntPtr p, int tamanho)
        {
            for (int i = 0; i < tamanho; i++) Marshal.WriteByte(p, i, 0);
        }
    }
}
