using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace Library
{
    public unsafe static class Unsafe
    {
#if Windows
        private static NativeLibraryManager _nativeLibraryManager;

        [SuppressUnmanagedCodeSecurity]
        private delegate void CopyDelegate(byte* source, byte* destination, int len);
        [SuppressUnmanagedCodeSecurity]
        [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool EqualsDelegate(byte* source1, byte* source2, int len);
        [SuppressUnmanagedCodeSecurity]
        private delegate int CompareDelegate(byte* source1, byte* source2, int len);
        [SuppressUnmanagedCodeSecurity]
        private delegate void XorDelegate(byte* source1, byte* source2, byte* result, int len);

        private static CopyDelegate _copy;
        private static EqualsDelegate _equals;
        private static CompareDelegate _compare;
        private static XorDelegate _xor;
#endif

        static Unsafe()
        {
#if Windows
            try
            {
                if (System.Environment.Is64BitProcess)
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_x64.dll");
                }
                else
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_x86.dll");
                }

                _copy = _nativeLibraryManager.GetMethod<CopyDelegate>("copy");
                _equals = _nativeLibraryManager.GetMethod<EqualsDelegate>("equals");
                _compare = _nativeLibraryManager.GetMethod<CompareDelegate>("compare");
                _xor = _nativeLibraryManager.GetMethod<XorDelegate>("xor");
            }
            catch (Exception e)
            {
                Log.Warning(e);
            }
#endif
        }

        public new static bool Equals(object obj1, object obj2)
        {
            throw new NotImplementedException();
        }

        public static void Zero(byte[] source)
        {
            Array.Clear(source, 0, source.Length);
        }

        public static void Zero(byte[] source, int index, int length)
        {
            Array.Clear(source, index, length);
        }

        public static void Copy(byte[] source, int sourceIndex, byte[] destination, int destinationIndex, int length)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            if (0 > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            if (0 > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            if (length > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException(nameof(length));
            if (length > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException(nameof(length));

            if (length == 0) return;

            fixed (byte* p_x = source)
            fixed (byte* p_y = destination)
            {
                byte* t_x = p_x + sourceIndex;
                byte* t_y = p_y + destinationIndex;

                _copy(t_x, t_y, length);
                //Marshal.Copy(new IntPtr((void*)t_x), destination, destinationIndex, length);
            }

            //Array.Copy(source, sourceIndex, destination, destinationIndex, length);
        }

        // Copyright (c) 2008-2013 Hafthor Stefansson
        // Distributed under the MIT/X11 software license
        // Ref: http://www.opensource.org/licenses/mit-license.php.
        // http://stackoverflow.com/questions/43289/comparing-two-byte-arrays-in-net
        public static bool Equals(byte[] source1, byte[] source2)
        {
            if (source1 == null) throw new ArgumentNullException(nameof(source1));
            if (source2 == null) throw new ArgumentNullException(nameof(source2));

            if (object.ReferenceEquals(source1, source2)) return true;
            if (source1.Length != source2.Length) return false;

            int length = source1.Length;

            fixed (byte* p_x = source1, p_y = source2)
            {
                return _equals(p_x, p_y, length);
            }
        }

        public static bool Equals(byte[] source1, int source1Index, byte[] source2, int source2Index, int length)
        {
            if (source1 == null) throw new ArgumentNullException(nameof(source1));
            if (source2 == null) throw new ArgumentNullException(nameof(source2));

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException(nameof(source1Index));
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException(nameof(source2Index));
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException(nameof(length));
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException(nameof(length));

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + source1Index, t_y = p_y + source2Index;

                return _equals(t_x, t_y, length);
            }
        }

        public static int Compare(byte[] source1, byte[] source2)
        {
            if (source1 == null) throw new ArgumentNullException(nameof(source1));
            if (source2 == null) throw new ArgumentNullException(nameof(source2));

            if (source1.Length != source2.Length) return (source1.Length > source2.Length) ? 1 : -1;

            if (source1.Length == 0) return 0;

            // �l�C�e�B�u�Ăяo���̑O�ɁA�Œ���̔�r���s���B
            {
                int c;
                if ((c = source1[0] - source2[0]) != 0) return c;
            }

            var length = source1.Length - 1;
            if (length == 0) return 0;

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + 1, t_y = p_y + 1;

                return _compare(t_x, t_y, length);
            }
        }

        internal static int Compare2(byte[] source1, byte[] source2)
        {
            if (source1 == null) throw new ArgumentNullException(nameof(source1));
            if (source2 == null) throw new ArgumentNullException(nameof(source2));

            if (source1.Length != source2.Length) return (source1.Length > source2.Length) ? 1 : -1;

            if (source1.Length == 0) return 0;

            fixed (byte* p_x = source1, p_y = source2)
            {
                return _compare(p_x, p_y, source1.Length);
            }
        }

        public static int Compare(byte[] source1, int source1Index, byte[] source2, int source2Index, int length)
        {
            if (source1 == null) throw new ArgumentNullException(nameof(source1));
            if (source2 == null) throw new ArgumentNullException(nameof(source2));

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException(nameof(source1Index));
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException(nameof(source2Index));
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException(nameof(length));
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException(nameof(length));

            if (length == 0) return 0;

            // �l�C�e�B�u�Ăяo���̑O�ɁA�Œ���̔�r���s���B
            {
                int c;
                if ((c = source1[source1Index] - source2[source2Index]) != 0) return c;
            }

            length--;

            if (length == 0) return 0;

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + source1Index + 1, t_y = p_y + source2Index + 1;

                return _compare(t_x, t_y, length);
            }
        }

        internal static int Compare2(byte[] source1, int source1Index, byte[] source2, int source2Index, int length)
        {
            if (source1 == null) throw new ArgumentNullException(nameof(source1));
            if (source2 == null) throw new ArgumentNullException(nameof(source2));

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException(nameof(source1Index));
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException(nameof(source2Index));
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException(nameof(length));
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException(nameof(length));

            if (length == 0) return 0;

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + source1Index, t_y = p_y + source2Index;

                return _compare(t_x, t_y, length);
            }
        }

        public static void Xor(byte[] source1, byte[] source2, byte[] destination)
        {
            if (source1 == null) throw new ArgumentNullException(nameof(source1));
            if (source2 == null) throw new ArgumentNullException(nameof(source2));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            // Zero
            {
                int targetRange = Math.Max(source1.Length, source2.Length);

                if (destination.Length > targetRange)
                {
                    Unsafe.Zero(destination, targetRange, destination.Length - targetRange);
                }
            }

            if (source1.Length > source2.Length && destination.Length > source2.Length)
            {
                Unsafe.Copy(source1, source2.Length, destination, source2.Length, Math.Min(source1.Length, destination.Length) - source2.Length);
            }
            else if (source2.Length > source1.Length && destination.Length > source1.Length)
            {
                Unsafe.Copy(source2, source1.Length, destination, source1.Length, Math.Min(source2.Length, destination.Length) - source1.Length);
            }

            int length = Math.Min(Math.Min(source1.Length, source2.Length), destination.Length);

            fixed (byte* p_x = source1, p_y = source2)
            {
                fixed (byte* p_buffer = destination)
                {
                    _xor(p_x, p_y, p_buffer, length);
                }
            }
        }

        public static void Xor(byte[] source1, int source1Index, byte[] source2, int source2Index, byte[] destination, int destinationIndex, int length)
        {
            if (source1 == null) throw new ArgumentNullException(nameof(source1));
            if (source2 == null) throw new ArgumentNullException(nameof(source2));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException(nameof(source1Index));
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException(nameof(source2Index));
            if (0 > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException(nameof(length));
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException(nameof(length));
            if (length > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException(nameof(length));

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + source1Index, t_y = p_y + source2Index;

                fixed (byte* p_buffer = destination)
                {
                    byte* t_buffer = p_buffer + destinationIndex;

                    _xor(t_x, t_y, t_buffer, length);
                }
            }
        }
    }
}
