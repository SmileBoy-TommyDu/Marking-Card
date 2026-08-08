using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace DrSoft.Drawing.Controls.DXFHelper.Parser
{
    /// <summary>
    /// DXF group code pair reader with automatic encoding detection.
    ///
    /// Encoding detection strategy:
    ///   1. Scan HEADER for $DWGCODEPAGE — if ANSI_936, always use GBK
    ///   2. Scan HEADER for $ACADVER — AC1021+ means UTF-8, earlier means GBK
    ///   3. If neither found, default to GBK (most common for Chinese DXF files)
    ///
    /// This handles Chinese CAD software (Zhongwang, Haochen) that uses GBK
    /// even with AC1021+ version numbers.
    /// </summary>
    internal sealed class GroupReader : IDisposable
    {
        private static readonly NumberFormatInfo Inv = CultureInfo.InvariantCulture.NumberFormat;

        private readonly Stream       _stream;
        private readonly StreamReader _sr;

        public long StreamLength   => _stream.Length;
        public long StreamPosition => _stream.Position;

        public GroupReader(string path)
        {
            _stream = new FileStream(path,
                FileMode.Open, FileAccess.Read, FileShare.Read,
                1 << 17, FileOptions.SequentialScan);

            var encoding = DetectEncoding(_stream);
            _stream.Position = 0;

            _sr = new StreamReader(_stream,
                encoding,
                detectEncodingFromByteOrderMarks: true,
                1 << 15, leaveOpen: false);
        }

        /// <summary>
        /// Pre-scan DXF HEADER for $DWGCODEPAGE and $ACADVER to determine encoding.
        ///
        /// Priority:
        ///   $DWGCODEPAGE = ANSI_936 → GBK (regardless of version)
        ///   $ACADVER >= AC1021      → UTF-8
        ///   $ACADVER <  AC1021      → GBK
        ///   neither found           → GBK (safe default for Chinese DXF)
        /// </summary>
        private static Encoding DetectEncoding(Stream stream)
        {
            try
            {
                long startPos = stream.Position;
                // Use Latin1 (ISO-8859-1) as safe intermediate: never throws on raw bytes,
                // so we can read group codes and ASCII header values without decoding errors.
                // We only need to match ASCII keywords like "$ACADVER", "$DWGCODEPAGE", "AC1021".
                using var tmpReader = new StreamReader(stream,
                    Encoding.GetEncoding(28591), // ISO-8859-1 (Latin-1)
                    detectEncodingFromByteOrderMarks: true,
                    1 << 12,
                    leaveOpen: true);

                bool foundHeader = false;
                string? acadVer = null;
                string? dwgCodePage = null;

                // Read up to 400 lines (HEADER is usually within first 100 lines)
                for (int i = 0; i < 400; i++)
                {
                    var line = tmpReader.ReadLine();
                    if (line == null) break;

                    var trimmed = line.AsSpan().Trim();

                    if (trimmed.SequenceEqual("HEADER"))
                    {
                        foundHeader = true;
                        continue;
                    }

                    if (trimmed.SequenceEqual("ENDSEC"))
                        break;

                    if (foundHeader && trimmed.SequenceEqual("$ACADVER"))
                    {
                        // Next pair: code=1, value=version string
                        tmpReader.ReadLine(); // skip code line
                        var verLine = tmpReader.ReadLine();
                        if (verLine != null)
                            acadVer = verLine.Trim();
                        // Don't break — continue scanning for $DWGCODEPAGE
                        continue;
                    }

                    if (foundHeader && trimmed.SequenceEqual("$DWGCODEPAGE"))
                    {
                        // Next pair: code=3, value=code page string
                        tmpReader.ReadLine(); // skip code line
                        var cpLine = tmpReader.ReadLine();
                        if (cpLine != null)
                            dwgCodePage = cpLine.Trim();
                        break; // $DWGCODEPAGE is usually after $ACADVER
                    }
                }

                stream.Position = startPos;

                // $DWGCODEPAGE takes highest priority — it explicitly declares the encoding
                if (!string.IsNullOrEmpty(dwgCodePage))
                {
                    if (dwgCodePage.Equals("ANSI_936", StringComparison.OrdinalIgnoreCase))
                        return Encoding.GetEncoding(936); // GBK
                    if (dwgCodePage.Equals("ANSI_65001", StringComparison.OrdinalIgnoreCase))
                        return Encoding.UTF8;
                    // Other code pages: try to map by number
                    if (dwgCodePage.StartsWith("ANSI_", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(dwgCodePage.AsSpan(5), out int cp))
                    {
                        try { return Encoding.GetEncoding(cp); }
                        catch { /* unknown codepage, fall through */ }
                    }
                }

                // Fall back to version-based detection
                if (acadVer != null && acadVer.Length >= 6)
                {
                    if (string.Compare(acadVer, "AC1021", StringComparison.Ordinal) >= 0)
                        return Encoding.UTF8;
                    else
                        return Encoding.GetEncoding(936); // GBK
                }

                // Default: GBK (safest for Chinese DXF files)
                return Encoding.GetEncoding(936);
            }
            catch
            {
                return Encoding.GetEncoding(936);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRead(out int code, out string value)
        {
            var codeLine = _sr.ReadLine();
            if (codeLine == null) { code = -1; value = ""; return false; }

            var valLine = _sr.ReadLine();
            if (valLine == null) { code = -1; value = ""; return false; }

            code  = ParseInt(codeLine.AsSpan());
            value = valLine;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ParseInt(ReadOnlySpan<char> s)
        {
            int i = 0, n = s.Length;
            while (i < n && s[i] == ' ') i++;
            int sign = 1;
            if (i < n && s[i] == '-') { sign = -1; i++; }
            int v = 0;
            while (i < n)
            {
                char c = s[i++];
                if (c < '0' || c > '9') break;
                v = v * 10 + (c - '0');
            }
            return sign * v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToDouble(string s)
            => double.Parse(s.AsSpan().Trim(), NumberStyles.Float, Inv);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToInt(string s)
            => int.Parse(s.AsSpan().Trim(), NumberStyles.Integer, Inv);

        public void Dispose() { _sr.Dispose(); _stream.Dispose(); }
    }
}
