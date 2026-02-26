// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using System;
using AxOS.Core;
using AxOS.Brain;
using AxOS.Kernel;
using AxOS.Hardware;
using AxOS.Storage;
using AxOS.Diagnostics;
using System.Text;
using Cosmos.Core.IOGroup;

namespace AxOS.Hardware
{
    public sealed class PeripheralNerve
    {
        private readonly COM _com1;
        private readonly StringBuilder _serialLineBuffer;
        private bool _isReady;

        public bool IsReady => _isReady;

        public PeripheralNerve()
        {
            _com1 = new COM(1);
            _serialLineBuffer = new StringBuilder(256);
        }

        public void Initialize()
        {
            try
            {
                _com1.InterruptEnable.Byte = 0x00;
                _com1.LineControl.Byte = 0x80;
                _com1.Data.Byte = 0x01;
                _com1.InterruptEnable.Byte = 0x00;
                _com1.LineControl.Byte = 0x03;
                _com1.FIFOControl.Byte = 0xC7;
                _com1.ModemControl.Byte = 0x0B;
                _isReady = true;
            }
            catch
            {
                _isReady = false;
            }
        }

        public bool TryReadLine(out string line)
        {
            line = string.Empty;
            if (!_isReady)
            {
                return false;
            }

            while (CanRead())
            {
                byte raw = _com1.Data.Byte;
                char c = (char)raw;

                if (c == '\r' || c == '\n')
                {
                    if (_serialLineBuffer.Length == 0)
                    {
                        continue;
                    }

                    line = _serialLineBuffer.ToString();
                    _serialLineBuffer.Clear();
                    return true;
                }

                if (c == '\b' || c == 127)
                {
                    if (_serialLineBuffer.Length > 0)
                    {
                        _serialLineBuffer.Length--;
                    }
                    continue;
                }

                if (c >= ' ' && c <= '~')
                {
                    if (_serialLineBuffer.Length < 512)
                    {
                        _serialLineBuffer.Append(c);
                    }
                }
            }

            return false;
        }

        private bool CanRead()
        {
            return (_com1.LineStatus.Byte & 0x01) != 0;
        }

        private bool CanWrite()
        {
            return (_com1.LineStatus.Byte & 0x20) != 0;
        }

        public void Write(string text)
        {
            if (!_isReady || text == null)
            {
                return;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    WriteChar('\r');
                }
                WriteChar(c);
            }
        }

        public void WriteLine(string text)
        {
            Write(text ?? string.Empty);
            Write("\n");
        }

        private void WriteChar(char c)
        {
            if (!_isReady)
            {
                return;
            }

            while (!CanWrite())
            {
            }

            _com1.Data.Byte = (byte)c;
        }
    }
}

