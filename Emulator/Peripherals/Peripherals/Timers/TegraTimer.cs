//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//

using Emul8.Core;
using Emul8.Logging;
using Emul8.Peripherals.Bus;
using Emul8.Time;
using System;

namespace Emul8.Peripherals.Timers
{
    public sealed class TegraTimer : IDoubleWordPeripheral, IKnownSize
	{
        public TegraTimer(Machine machine)
        {
            IRQ = new GPIO();
            sync = new object();
            clockSource = machine.ObtainClockSource();
            Reset();
        }

        public GPIO IRQ { get; private set; }

		public uint ReadDoubleWord(long offset)
		{			
			lock(sync)
            { 
                var clockEntry = clockSource.GetClockEntry(OnLimitReached);
                var value = (uint)clockEntry.Period;
                switch((Registers)offset)
                {
                case Registers.Ptv:                   
                    if(clockEntry.Enabled)
                    {
                        value |= 0x80000000;
                    }
                    if(clockEntry.WorkMode == WorkMode.Periodic)
                    {
                        value |= 0x40000000;
                    }
                    this.NoisyLog("Read at Ptv, 0x{1:X}, 0x{2:X}, {0}%.", value*100/clockEntry.Period, value, clockEntry.Period);
                    return value;
                case Registers.Pcr:
                    this.NoisyLog("Read at Pcr, 0x{1:X}, 0x{2:X}, {0}%.", value*100/clockEntry.Period, value, clockEntry.Period);
                    return (uint)clockEntry.Value;
                default:
                    this.LogUnhandledRead(offset);
                    return 0;
                }
            }
		}
		
		public void WriteDoubleWord(long offset, uint value)
        {
            lock(sync)
            {
                switch((Registers)offset)
                {
                case Registers.Ptv:
                    clockSource.ExchangeClockEntryWith(OnLimitReached, oldEntry => oldEntry.With(period: (value & ((1 << 29) - 1)) + 1,
                        workMode: (0x40000000 & value) != 0 ? WorkMode.Periodic : WorkMode.OneShot,
                        enabled: (0x80000000 & value) != 0));
                    break;
                case Registers.Pcr:
                    if((0x40000000 & value) != 0)
                    {
                        IRQ.Unset();
                    }
                    break;
                default:
                    this.LogUnhandledWrite(offset, value);
                    break;
                }        
            }
		}

        public long Size
        {
            get
            {
                return 8;
            }
        }

        private void OnLimitReached()
        {
            this.Log(LogLevel.Noisy, "Alarm on tmr, value 0x{0:X}", clockSource.GetClockEntry(OnLimitReached).Value);
            IRQ.Set();
        }

        public void Reset()
        {
            var clockEntry = new ClockEntry((1 << 29) - 1, ClockEntry.FrequencyToRatio(this, 1000000), OnLimitReached, false) { Value = 0 };
            clockSource.ExchangeClockEntryWith(OnLimitReached, x => clockEntry, () => clockEntry);
        }

        private readonly IClockSource clockSource;

        private enum Registers
        {
            Ptv = 0x0,
            Pcr = 0x4
        }

        private readonly object sync;
	}
}

