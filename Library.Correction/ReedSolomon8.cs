using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Library.Correction
{
    public class ReedSolomon8 : ManagerBase, IThisLock
    {
        private volatile ReedSolomon8.Math _fecMath;
        private volatile int _k;
        private volatile int _n;
        private volatile int _threadCount;
        private volatile BufferManager _bufferManager;

        private volatile byte[] _encMatrix;

        private volatile bool _cancel;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public ReedSolomon8(int k, int n, int threadCount, BufferManager bufferManager)
        {
            _fecMath = new Math();
            _k = k;
            _n = n;
            _threadCount = threadCount;
            _bufferManager = bufferManager;

            _encMatrix = _fecMath.CreateEncodeMatrix(k, n);
        }

        public void Encode(ArraySegment<byte>[] sources, ArraySegment<byte>[] repairs, int[] index, int packetLength)
        {
            _cancel = false;

            lock (this.ThisLock)
            {
                Parallel.For(0, repairs.Length, new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, row =>
                {
                    if (_cancel) return;

                    Thread.CurrentThread.IsBackground = true;
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                    // *remember* indices start at 0, k starts at 1.
                    if (index[row] < _k)
                    {
                        // < k, systematic so direct copy.
                        Unsafe.Copy(sources[index[row]].Array, sources[index[row]].Offset, repairs[row].Array, repairs[row].Offset, packetLength);
                    }
                    else
                    {
                        // index[row] >= k && index[row] < n
                        int pos = index[row] * _k;
                        Unsafe.Zero(repairs[row].Array, repairs[row].Offset, packetLength);

                        for (int col = 0; col < _k; col++)
                        {
                            if (_cancel) return;

                            _fecMath.AddMul(repairs[row].Array, repairs[row].Offset, sources[col].Array, sources[col].Offset, _encMatrix[pos + col], packetLength);
                        }
                    }
                });
            }
        }

        public void Decode(ArraySegment<byte>[] packets, int[] index, int packetLength)
        {
            _cancel = false;

            lock (this.ThisLock)
            {
                ReedSolomon8.Shuffle(packets, index, _k);

                byte[] decMatrix = _fecMath.CreateDecodeMatrix(_encMatrix, index, _k, _n);

                // do the actual decoding..
                byte[][] tempPackets = new byte[_k][];

                Parallel.For(0, _k, new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, row =>
                {
                    if (_cancel) return;

                    Thread.CurrentThread.IsBackground = true;
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                    if (index[row] >= _k)
                    {
                        tempPackets[row] = _bufferManager.TakeBuffer(packetLength);
                        Unsafe.Zero(tempPackets[row], 0, packetLength);

                        for (int col = 0; col < _k; col++)
                        {
                            if (_cancel) return;

                            _fecMath.AddMul(tempPackets[row], 0, packets[col].Array, packets[col].Offset, decMatrix[row * _k + col], packetLength);
                        }
                    }
                });

                if (_cancel) return;

                // move pkts to their final destination
                for (int row = 0; row < _k; row++)
                {
                    if (index[row] >= _k)
                    {
                        // only copy those actually decoded.
                        Unsafe.Copy(tempPackets[row], 0, packets[row].Array, packets[row].Offset, packetLength);
                        index[row] = row;
                        _bufferManager.ReturnBuffer(tempPackets[row]);
                    }
                }
            }
        }

        private static void Shuffle(ArraySegment<byte>[] packets, int[] index, int k)
        {
            for (int i = 0; i < k;)
            {
                if (index[i] >= k || index[i] == i)
                {
                    i++;
                }
                else
                {
                    // put pkts in the right position (first check for conflicts).
                    int c = index[i];

                    if (index[c] == c)
                    {
                        throw new ArgumentException("Shuffle error at " + i);
                    }

                    // swap(index[c],index[i])
                    int tmp = index[i];
                    index[i] = index[c];
                    index[c] = tmp;

                    // swap(pkts[c],pkts[i])
                    ArraySegment<byte> tmp2 = packets[i];
                    packets[i] = packets[c];
                    packets[c] = tmp2;
                }
            }
        }

        public void Cancel()
        {
            _cancel = true;
        }

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion

        private unsafe class Math : ManagerBase
        {
            private NativeLibraryManager _nativeLibraryManager;

            delegate void MulDelegate(byte* src, byte* dst, byte* mulc, int len);
            private MulDelegate _mul;

            private const int _gfBits = 8;

            private volatile int _gfSize;

            /**
             * Primitive polynomials - see Lin & Costello, Appendix A,
             * and  Lee & Messerschmitt, p. 453.
             */
            private static string[] _prim_polys = {    
                                          // gfBits         polynomial
                null,                     // 0              no code
                null,                     // 1              no code
                "111",                    // 2              1+x+x^2
                "1101",	                  // 3              1+x+x^3
                "11001",       	          // 4              1+x+x^4
                "101001",      	          // 5              1+x^2+x^5
                "1100001",     	          // 6              1+x+x^6
                "10010001",    	          // 7              1+x^3+x^7
                "101110001",              // 8              1+x^2+x^3+x^4+x^8
                "1000100001",             // 9              1+x^4+x^9
                "10010000001",            // 10             1+x^3+x^10
                "101000000001",           // 11             1+x^2+x^11
                "1100101000001",          // 12             1+x+x^4+x^6+x^12
                "11011000000001",         // 13             1+x+x^3+x^4+x^13
                "110000100010001",        // 14             1+x+x^6+x^10+x^14
                "1100000000000001",       // 15             1+x+x^15
                "11010000000010001"       // 16             1+x+x^3+x^12+x^16
            };

            /**
             * To speed up computations, we have tables for logarithm, exponent
             * and inverse of a number. If gfBits &lt;= 8, we use a table for
             * multiplication as well (it takes 64K, no big deal even on a PDA,
             * especially because it can be pre-initialized an put into a ROM!),
             * otherwhise we use a table of logarithms.
             */

            // index->poly form conversion table
            private volatile byte[] _gf_exp;
            // Poly->index form conversion table
            private volatile int[] _gf_log;
            // inverse of field elem.
            private volatile byte[] _inverse;

            /**
             * gf_mul(x,y) multiplies two numbers. If gfBits&lt;=8, it is much
             * faster to use a multiplication table.
             *
             * USE_GF_MULC, GF_MULC0(c) and GF_ADDMULC(x) can be used when multiplying
             * many numbers by the same constant. In this case the first
             * call sets the constant, and others perform the multiplications.
             * A value related to the multiplication is held in a local variable
             * declared with USE_GF_MULC . See usage in addMul1().
             */
            private volatile byte[][] _gf_mul_table;

            private volatile bool _disposed;

            public Math()
            {
                try
                {
#if Windows
                    if (System.Environment.Is64BitProcess)
                    {
                        _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_Correction_x64.dll");
                    }
                    else
                    {
                        _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_Correction_x86.dll");
                    }
#endif

#if Unix
                    if (System.Environment.Is64BitProcess)
                    {
                        _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_Correction_x64.so");
                    }
                    else
                    {
                        _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_Correction_x86.so");
                    }
#endif

                    _mul = _nativeLibraryManager.GetMethod<MulDelegate>("mul");
                }
                catch (Exception e)
                {
                    Log.Warning(e);
                }

                _gfSize = ((1 << _gfBits) - 1);

                _gf_exp = new byte[2 * _gfSize];
                _gf_log = new int[_gfSize + 1];
                _inverse = new byte[_gfSize + 1];

                this.GenerateGF();
                this.InitMulTable();
            }

            public void GenerateGF()
            {
                string primPoly = _prim_polys[_gfBits];
                var mask = (byte)1;	// x ** 0 = 1
                _gf_exp[_gfBits] = (byte)0; // will be updated at the end of the 1st loop

                /*
                 * first, generate the (polynomial representation of) powers of \alpha,
                 * which are stored in gf_exp[i] = \alpha ** i .
                 * At the same time build gf_log[gf_exp[i]] = i .
                 * The first gfBits powers are simply bits shifted to the left.
                 */
                for (int i = 0; i < _gfBits; i++, mask <<= 1)
                {
                    _gf_exp[i] = mask;
                    _gf_log[_gf_exp[i]] = i;

                    /*
                     * If primPoly[i] == 1 then \alpha ** i occurs in poly-repr
                     * gf_exp[gfBits] = \alpha ** gfBits
                     */
                    if (primPoly[i] == '1')
                    {
                        _gf_exp[_gfBits] ^= mask;
                    }
                }

                /*
                 * now gf_exp[gfBits] = \alpha ** gfBits is complete, so can als
                 * compute its inverse.
                 */
                _gf_log[_gf_exp[_gfBits]] = _gfBits;

                /*
                 * Poly-repr of \alpha ** (i+1) is given by poly-repr of
                 * \alpha ** i shifted left one-bit and accounting for any
                 * \alpha ** gfBits term that may occur when poly-repr of
                 * \alpha ** i is shifted.
                 */
                mask = (byte)(1 << (_gfBits - 1));

                for (int i = _gfBits + 1; i < _gfSize; i++)
                {
                    if (_gf_exp[i - 1] >= mask)
                    {
                        _gf_exp[i] = (byte)(_gf_exp[_gfBits] ^ ((_gf_exp[i - 1] ^ mask) << 1));
                    }
                    else
                    {
                        _gf_exp[i] = (byte)(_gf_exp[i - 1] << 1);
                    }
                    _gf_log[_gf_exp[i]] = i;
                }

                /*
                 * log(0) is not defined, so use a special value
                 */
                _gf_log[0] = _gfSize;

                // set the extended gf_exp values for fast multiply
                for (int i = 0; i < _gfSize; i++)
                {
                    _gf_exp[i + _gfSize] = _gf_exp[i];
                }

                /*
                 * again special cases. 0 has no inverse. This used to
                 * be initialized to gfSize, but it should make no difference
                 * since noone is supposed to read from here.
                 */
                _inverse[0] = (byte)0;
                _inverse[1] = (byte)1;

                for (int i = 2; i <= _gfSize; i++)
                {
                    _inverse[i] = _gf_exp[_gfSize - _gf_log[i]];
                }
            }

            public void InitMulTable()
            {
                _gf_mul_table = new byte[_gfSize + 1][];

                for (int i = 0; i < _gfSize + 1; i++)
                {
                    _gf_mul_table[i] = new byte[_gfSize + 1];
                }

                for (int i = 0; i < _gfSize + 1; i++)
                {
                    for (int j = 0; j < _gfSize + 1; j++)
                    {
                        _gf_mul_table[i][j] = _gf_exp[Modnn(_gf_log[i] + _gf_log[j])];
                    }
                }

                for (int i = 0; i < _gfSize + 1; i++)
                {
                    _gf_mul_table[0][i] = (byte)0;
                    _gf_mul_table[i][0] = (byte)0;
                }
            }

            public byte Modnn(int x)
            {
                while (x >= _gfSize)
                {
                    x -= _gfSize;
                    x = (x >> _gfBits) + (x & _gfSize);
                }

                return (byte)x;
            }

            public byte Mul(byte x, byte y)
            {
                return _gf_mul_table[x][y];
            }

            public static byte[] CreateGFMatrix(int rows, int cols)
            {
                return new byte[rows * cols];
            }

            public void AddMul(byte[] dst, int dstPos, byte[] src, int srcPos, byte c, int len)
            {
                // nop, optimize
                if (c == 0) return;

                // use our multiplication table.
                // Instead of doing gf_mul_table[c,x] for multiply, we'll save
                // the gf_mul_table[c] to a local variable since it is going to
                // be used many times.
                byte[] gf_mulc = _gf_mul_table[c];

                try
                {
                    fixed (byte* p_dst = dst)
                    fixed (byte* p_src = src)
                    fixed (byte* p_gf_mulc = gf_mulc)
                    {
                        _mul(p_src + srcPos, p_dst + dstPos, p_gf_mulc, len);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }

            public void MatMul(byte[] a, int aStart, byte[] b, int bStart, byte[] c, int cStart, int n, int k, int m)
            {
                for (int row = 0; row < n; row++)
                {
                    for (int col = 0; col < m; col++)
                    {
                        int posA = row * k;
                        int posB = col;
                        var acc = (byte)0;

                        for (int i = 0; i < k; i++, posA++, posB += m)
                        {
                            acc ^= this.Mul(a[aStart + posA], b[bStart + posB]);
                        }

                        c[cStart + (row * m + col)] = acc;
                    }
                }
            }

            public void InvertMatrix(byte[] src, int k)
            {
                int[] indxc = new int[k];
                int[] indxr = new int[k];

                // ipiv marks elements already used as pivots.
                int[] ipiv = new int[k];

                byte[] id_row = Math.CreateGFMatrix(1, k);
                //byte[] temp_row = CreateGFMatrix(1, k);

                for (int col = 0; col < k; col++)
                {
                    /*
                     * Zeroing column 'col', look for a non-zero element.
                     * First try on the diagonal, if it fails, look elsewhere.
                     */
                    int irow = -1;
                    int icol = -1;
                    bool foundPiv = false;

                    if (ipiv[col] != 1 && src[col * k + col] != 0)
                    {
                        irow = col;
                        icol = col;
                        foundPiv = true;
                    }

                    if (!foundPiv)
                    {
                        loop1:
                        for (int row = 0; row < k; row++)
                        {
                            if (ipiv[row] != 1)
                            {
                                for (int ix = 0; ix < k; ix++)
                                {
                                    if (ipiv[ix] == 0)
                                    {
                                        if (src[row * k + ix] != 0)
                                        {
                                            irow = row;
                                            icol = ix;
                                            foundPiv = true;
                                            goto loop1;
                                        }
                                    }
                                    else if (ipiv[ix] > 1)
                                    {
                                        throw new ArgumentException("singular matrix");
                                    }
                                }
                            }
                        }
                    }

                    // redundant??? I'm too lazy to figure it out -Justin
                    if (!foundPiv && icol == -1)
                    {
                        throw new ArgumentException("XXX pivot not found!");
                    }

                    ipiv[icol] = ipiv[icol] + 1;

                    /*
                     * swap rows irow and icol, so afterwards the diagonal
                     * element will be correct. Rarely done, not worth
                     * optimizing.
                     */
                    if (irow != icol)
                    {
                        for (int ix = 0; ix < k; ix++)
                        {
                            // swap 'em
                            byte tmp = src[irow * k + ix];
                            src[irow * k + ix] = src[icol * k + ix];
                            src[icol * k + ix] = tmp;
                        }
                    }

                    indxr[col] = irow;
                    indxc[col] = icol;

                    int pivotRowPos = icol * k;
                    byte c = src[pivotRowPos + icol];

                    if (c == 0)
                    {
                        throw new ArgumentException("singular matrix 2");
                    }

                    if (c != 1)
                    {
                        /* otherwhise this is a NOP */
                        /*
                         * this is done often , but optimizing is not so
                         * fruitful, at least in the obvious ways (unrolling)
                         */
                        c = _inverse[c];
                        src[pivotRowPos + icol] = (byte)1;

                        for (int ix = 0; ix < k; ix++)
                        {
                            src[pivotRowPos + ix] = Mul(c, src[pivotRowPos + ix]);
                        }
                    }

                    /*
                     * from all rows, remove multiples of the selected row
                     * to zero the relevant entry (in fact, the entry is not zero
                     * because we know it must be zero).
                     * (Here, if we know that the pivotRowPos is the identity,
                     * we can optimize the addMul).
                     */
                    id_row[icol] = (byte)1;

                    if (!Unsafe.Equals(src, pivotRowPos, id_row, 0, k))
                    {
                        for (int p = 0, ix = 0; ix < k; ix++, p += k)
                        {
                            if (ix != icol)
                            {
                                c = src[p + icol];
                                src[p + icol] = (byte)0;
                                this.AddMul(src, p, src, pivotRowPos, c, k);
                            }
                        }
                    }

                    id_row[icol] = (byte)0;
                } // done all columns

                for (int col = k - 1; col >= 0; col--)
                {
                    if (indxr[col] < 0 || indxr[col] >= k)
                    {
                        //System.err.println("AARGH, indxr[col] "+indxr[col]);
                    }
                    else if (indxc[col] < 0 || indxc[col] >= k)
                    {
                        //System.err.println("AARGH, indxc[col] "+indxc[col]);
                    }
                    else
                    {
                        if (indxr[col] != indxc[col])
                        {
                            for (int row = 0; row < k; row++)
                            {
                                // swap 'em
                                byte tmp = src[row * k + indxc[col]];
                                src[row * k + indxc[col]] = src[row * k + indxr[col]];
                                src[row * k + indxr[col]] = tmp;
                            }
                        }
                    }
                }
            }

            public void InvertVandermonde(byte[] src, int k)
            {
                if (k == 1)
                {
                    // degenerate case, matrix must be p^0 = 1
                    return;
                }

                /*
                 * c holds the coefficient of P(x) = Prod (x - p_i), i=0..k-1
                 * b holds the coefficient for the matrix inversion
                 */
                byte[] c = Math.CreateGFMatrix(1, k);
                byte[] b = Math.CreateGFMatrix(1, k);
                byte[] p = Math.CreateGFMatrix(1, k);

                for (int j = 1, i = 0; i < k; i++, j += k)
                {
                    c[i] = (byte)0;
                    p[i] = src[j];    /* p[i] */
                }

                /*
                 * construct coeffs. recursively. We know c[k] = 1 (implicit)
                 * and start P_0 = x - p_0, then at each stage multiply by
                 * x - p_i generating P_i = x P_{i-1} - p_i P_{i-1}
                 * After k steps we are done.
                 */
                c[k - 1] = p[0];	/* really -p(0), but x = -x in GF(2^m) */

                for (int i = 1; i < k; i++)
                {
                    byte p_i = p[i]; /* see above comment */

                    for (int j = k - 1 - (i - 1); j < k - 1; j++)
                    {
                        c[j] ^= Mul(p_i, c[j + 1]);
                    }

                    c[k - 1] ^= p_i;
                }

                for (int row = 0; row < k; row++)
                {
                    /*
                     * synthetic division etc.
                     */
                    byte xx = p[row];
                    var t = (byte)1;
                    b[k - 1] = (byte)1; /* this is in fact c[k] */

                    for (int i = k - 2; i >= 0; i--)
                    {
                        b[i] = (byte)(c[i + 1] ^ Mul(xx, b[i + 1]));
                        t = (byte)(Mul(xx, t) ^ b[i]);
                    }

                    for (int col = 0; col < k; col++)
                    {
                        src[col * k + row] = Mul(_inverse[t], b[col]);
                    }
                }
            }

            public byte[] CreateEncodeMatrix(int k, int n)
            {
                if (k > _gfSize + 1 || n > _gfSize + 1 || k > n)
                {
                    throw new ArgumentException("Invalid parameters n=" + n + ",k=" + k + ",gfSize=" + _gfSize);
                }

                byte[] encMatrix = Math.CreateGFMatrix(n, k);

                /*
                 * The encoding matrix is computed starting with a Vandermonde matrix,
                 * and then transforming it into a systematic matrix.
                 *
                 * fill the matrix with powers of field elements, starting from 0.
                 * The first row is special, cannot be computed with exp. table.
                 */
                byte[] tmpMatrix = Math.CreateGFMatrix(n, k);

                tmpMatrix[0] = (byte)1;

                // first row should be 0's, fill in the rest.
                for (int pos = k, row = 0; row < n - 1; row++, pos += k)
                {
                    for (int col = 0; col < k; col++)
                    {
                        tmpMatrix[pos + col] = _gf_exp[Modnn(row * col)];
                    }
                }

                /*
                 * quick code to build systematic matrix: invert the top
                 * k*k vandermonde matrix, multiply right the bottom n-k rows
                 * by the inverse, and construct the identity matrix at the top.
                 */
                // much faster than invertMatrix 
                this.InvertVandermonde(tmpMatrix, k);
                this.MatMul(tmpMatrix, k * k, tmpMatrix, 0, encMatrix, k * k, n - k, k, k);

                /*
                 * the upper matrix is I so do not bother with a slow multiply
                 */
                Unsafe.Zero(encMatrix, 0, k * k);

                for (int i = 0, col = 0; col < k; col++, i += k + 1)
                {
                    encMatrix[i] = (byte)1;
                }

                return encMatrix;
            }

            public byte[] CreateDecodeMatrix(byte[] encMatrix, int[] index, int k, int n)
            {
                byte[] matrix = CreateGFMatrix(k, k);

                for (int i = 0, pos = 0; i < k; i++, pos += k)
                {
                    Unsafe.Copy(encMatrix, index[i] * k, matrix, pos, k);
                }

                this.InvertMatrix(matrix, k);

                return matrix;
            }

            protected override void Dispose(bool disposing)
            {
                if (_disposed) return;
                _disposed = true;

                if (disposing)
                {
                    if (_nativeLibraryManager != null)
                    {
                        try
                        {
                            _nativeLibraryManager.Dispose();
                        }
                        catch (Exception)
                        {

                        }

                        _nativeLibraryManager = null;
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_fecMath != null)
                {
                    try
                    {
                        _fecMath.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _fecMath = null;
                }
            }
        }
    }
}
