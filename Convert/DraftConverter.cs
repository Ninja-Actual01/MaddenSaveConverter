using System.Text;

namespace MaddenSaveConverter.Convert;

public static class DraftConverter
{
    // Draft files: FBCHUNKS + file-level JSON + players
    // M26: firstName 17, lastName 21, hometown 28, stats 92, asset 32, gap 12
    // M27: firstName 21, lastName 49, stats 90, asset 32, gap 54
    // "Missing in Madden 27" is detected as: no name-matched player found in the M27 draft template.

    public static ConvertResult M26ToM27(string srcPath, string tplPath, string outPath, bool keepSourcePortraitWhenMissingInM27 = false, bool keepSourceAssetWhenMissingInM27 = false)
    {
        var res = new ConvertResult { OutputPath = outPath };
        void log(string s) => res.Log.Add(s);
        var src = File.ReadAllBytes(srcPath);
        var tpl = File.ReadAllBytes(tplPath);
        if (Encoding.ASCII.GetString(src, 0, 8) != "FBCHUNKS" || Encoding.ASCII.GetString(tpl, 0, 8) != "FBCHUNKS")
            throw new InvalidDataException("not FBCHUNKS");

        var srcPlayers = Parse(src, true);
        var tplPlayers = Parse(tpl, false);
        log($"M26 draft players: {srcPlayers.Count}");
        log($"M27 template players: {tplPlayers.Count}");
        log($"portrait IDs: {(keepSourcePortraitWhenMissingInM27 ? "keep source" : "use Madden 27 match/generic when missing")}");
        log($"asset names:  {(keepSourceAssetWhenMissingInM27 ? "keep source" : "use Madden 27 match/generic when missing")}");

        // name -> tpl player (for M27-mapped values) - keep first on duplicate names
        var tplByName = new Dictionary<string, DraftPlayer>();
        foreach (var p in tplPlayers) { var k = p.Fn + "|" + p.Ln; if (!tplByName.ContainsKey(k)) tplByName[k] = p; }
        var outBuf = (byte[])tpl.Clone();

        int n = Math.Min(srcPlayers.Count, tplPlayers.Count);
        int portraitFromMatch = 0, portraitFromSource = 0, portraitFromGeneric = 0;
        int assetFromMatch = 0, assetFromSource = 0, assetFromGeneric = 0;
        for (int i = 0; i < n; i++)
        {
            var s = srcPlayers[i];
            var d = tplPlayers[i];
            tplByName.TryGetValue(s.Fn + "|" + s.Ln, out var match);
            bool hasM27Match = match != null;

            // names
            var fnBytes = new byte[21]; Encoding.ASCII.GetBytes(s.Fn).CopyTo(fnBytes, 0); Array.Copy(fnBytes, 0, outBuf, d.Off, 21);
            var lnBytes = new byte[49]; Encoding.ASCII.GetBytes(s.Ln).CopyTo(lnBytes, 0); Array.Copy(lnBytes, 0, outBuf, d.Off + 21, 49);

            // asset 32
            if (keepSourceAssetWhenMissingInM27)
            {
                Array.Copy(src, s.AssetStart, outBuf, d.AssetStart, 32);
                if (hasM27Match) assetFromMatch++; else assetFromSource++;
            }
            else if (hasM27Match)
            {
                Array.Copy(tpl, match.AssetStart, outBuf, d.AssetStart, 32);
                assetFromMatch++;
            }
            else
            {
                Array.Copy(tpl, d.AssetStart, outBuf, d.AssetStart, 32); // generic/template slot
                assetFromGeneric++;
            }

            // stats: 0-64 from src, 64-90 portrait (M27 has 90; M26 source has 92)
            var m26Stats = src.AsSpan(s.StatsStart, 90).ToArray();
            byte[] portraitSrc;
            if (keepSourcePortraitWhenMissingInM27)
            {
                // Keep source portrait bytes (truncate M26's 28-byte portrait region to M27's 26 bytes)
                portraitSrc = src.AsSpan(s.StatsStart + 64, 26).ToArray();
                // M26 stores the draft portrait ID at stats+90 (uint16 LE); M27 uses stats+78.
                // Map it so MFT/in-game "Portrait ID" is preserved from source.
                if (s.StatsStart + 91 < src.Length)
                {
                    portraitSrc[14] = src[s.StatsStart + 90];
                    portraitSrc[15] = src[s.StatsStart + 91];
                }
                if (hasM27Match) portraitFromMatch++; else portraitFromSource++;
            }
            else if (hasM27Match)
            {
                portraitSrc = tpl.AsSpan(match.StatsStart + 64, 26).ToArray();
                portraitFromMatch++;
            }
            else
            {
                portraitSrc = tpl.AsSpan(d.StatsStart + 64, 26).ToArray(); // generic/template slot
                portraitFromGeneric++;
            }

            var newStats = new byte[90];
            Array.Copy(m26Stats, 0, newStats, 0, 64);
            Array.Copy(portraitSrc, 0, newStats, 64, 26);
            Array.Copy(newStats, 0, outBuf, d.StatsStart, 90);

            // appearance JSON (genericHeadName/loadouts/etc.) - key for in-game portraits
            if (keepSourcePortraitWhenMissingInM27)
            {
                int srcJsonLen = s.JsonEnd - s.JsonStart;
                int dstSlotSpace = d.Next - d.JsonStart;
                if (srcJsonLen <= dstSlotSpace)
                {
                    Array.Copy(src, s.JsonStart, outBuf, d.JsonStart, srcJsonLen);
                    Array.Clear(outBuf, d.JsonStart + srcJsonLen, dstSlotSpace - srcJsonLen);
                }
                else
                {
                    log($"  [warn] {s.Fn} {s.Ln}: source appearance JSON too large for slot ({srcJsonLen}>{dstSlotSpace}), keeping template JSON");
                }
            }
        }
        File.WriteAllBytes(outPath, outBuf);
        log($"transplanted {n} players M26->M27");
        log($"  portraits: kept/source={portraitFromSource + (keepSourcePortraitWhenMissingInM27 ? portraitFromMatch : 0)}, template-match={(!keepSourcePortraitWhenMissingInM27 ? portraitFromMatch : 0)}, generic(missing)={portraitFromGeneric}");
        log($"  assets:    kept/source={assetFromSource + (keepSourceAssetWhenMissingInM27 ? assetFromMatch : 0)}, template-match={(!keepSourceAssetWhenMissingInM27 ? assetFromMatch : 0)}, generic(missing)={assetFromGeneric}");
        res.Success = true;
        return res;
    }

    public static ConvertResult M27ToM26(string srcPath, string tplPath, string outPath, bool keepSourcePortraitWhenMissingInM26 = false, bool keepSourceAssetWhenMissingInM26 = false)
    {
        var res = new ConvertResult { OutputPath = outPath };
        void log(string s) => res.Log.Add(s);
        var src = File.ReadAllBytes(srcPath);
        var tpl = File.ReadAllBytes(tplPath);
        var srcPlayers = Parse(src, false);
        var tplPlayers = Parse(tpl, true);
        log($"M27 draft players: {srcPlayers.Count}");
        log($"M26 template players: {tplPlayers.Count}");
        log($"portrait IDs: {(keepSourcePortraitWhenMissingInM26 ? "keep source" : "use Madden 26 match/generic when missing")}");
        log($"asset names:  {(keepSourceAssetWhenMissingInM26 ? "keep source" : "use Madden 26 match/generic when missing")}");

        var tplByName = new Dictionary<string, DraftPlayer>();
        foreach (var p in tplPlayers) { var k = p.Fn + "|" + p.Ln; if (!tplByName.ContainsKey(k)) tplByName[k] = p; }
        var outBuf = (byte[])tpl.Clone();
        int n = Math.Min(srcPlayers.Count, tplPlayers.Count);
        int portraitFromMatch = 0, portraitFromSource = 0, portraitFromGeneric = 0;
        int assetFromMatch = 0, assetFromSource = 0, assetFromGeneric = 0;
        for (int i = 0; i < n; i++)
        {
            var s = srcPlayers[i];
            var d = tplPlayers[i];
            tplByName.TryGetValue(s.Fn + "|" + s.Ln, out var match);
            bool hasM26Match = match != null;

            var fnBytes = new byte[17]; Encoding.ASCII.GetBytes(s.Fn).CopyTo(fnBytes, 0); Array.Copy(fnBytes, 0, outBuf, d.Off, 17);
            var lnBytes = new byte[21]; Encoding.ASCII.GetBytes(s.Ln).CopyTo(lnBytes, 0); Array.Copy(lnBytes, 0, outBuf, d.Off + 17, 21);

            // hometown 28 bytes - keep from name-matched tpl if available
            if (hasM26Match)
                Array.Copy(tpl, match.Off + 38, outBuf, d.Off + 38, 28);
            else
                Array.Clear(outBuf, d.Off + 38, 28);

            // asset 32
            if (keepSourceAssetWhenMissingInM26)
            {
                Array.Copy(src, s.AssetStart, outBuf, d.AssetStart, 32);
                if (hasM26Match) assetFromMatch++; else assetFromSource++;
            }
            else if (hasM26Match)
            {
                Array.Copy(tpl, match.AssetStart, outBuf, d.AssetStart, 32);
                assetFromMatch++;
            }
            else
            {
                Array.Copy(tpl, d.AssetStart, outBuf, d.AssetStart, 32);
                assetFromGeneric++;
            }

            // stats: 0-64 from src, 64-92 portrait (M26 has 92)
            var m27Stats = src.AsSpan(s.StatsStart, 90).ToArray();
            byte[] portraitSrc;
            if (keepSourcePortraitWhenMissingInM26)
            {
                // Keep source portrait bytes (expand M27's 26-byte portrait region to M26's 28 bytes)
                portraitSrc = new byte[28];
                Array.Copy(src, s.StatsStart + 64, portraitSrc, 0, Math.Min(26, portraitSrc.Length));
                // M27 portrait ID is stored at stats+78 (uint16 LE); M26 stores it at stats+90.
                // Preserve source portrait ID in the correct M26 location.
                portraitSrc[26] = src[s.StatsStart + 78];
                portraitSrc[27] = src[s.StatsStart + 79];
                if (hasM26Match) portraitFromMatch++; else portraitFromSource++;
            }
            else if (hasM26Match)
            {
                portraitSrc = tpl.AsSpan(match.StatsStart + 64, 28).ToArray();
                portraitFromMatch++;
            }
            else
            {
                portraitSrc = tpl.AsSpan(d.StatsStart + 64, 28).ToArray();
                portraitFromGeneric++;
            }

            var newStats = new byte[92];
            Array.Copy(m27Stats, 0, newStats, 0, 64);
            Array.Copy(portraitSrc, 0, newStats, 64, Math.Min(portraitSrc.Length, 28));
            Array.Copy(newStats, 0, outBuf, d.StatsStart, 92);

            // appearance JSON (genericHeadName/loadouts/etc.) - key for in-game portraits
            if (keepSourcePortraitWhenMissingInM26)
            {
                int srcJsonLen = s.JsonEnd - s.JsonStart;
                int dstSlotSpace = d.Next - d.JsonStart;
                if (srcJsonLen <= dstSlotSpace)
                {
                    Array.Copy(src, s.JsonStart, outBuf, d.JsonStart, srcJsonLen);
                    Array.Clear(outBuf, d.JsonStart + srcJsonLen, dstSlotSpace - srcJsonLen);
                }
                else
                {
                    log($"  [warn] {s.Fn} {s.Ln}: source appearance JSON too large for slot ({srcJsonLen}>{dstSlotSpace}), keeping template JSON");
                }
            }
        }
        File.WriteAllBytes(outPath, outBuf);
        log($"transplanted {n} players M27->M26");
        log($"  portraits: kept/source={portraitFromSource + (keepSourcePortraitWhenMissingInM26 ? portraitFromMatch : 0)}, template-match={(!keepSourcePortraitWhenMissingInM26 ? portraitFromMatch : 0)}, generic(missing)={portraitFromGeneric}");
        log($"  assets:    kept/source={assetFromSource + (keepSourceAssetWhenMissingInM26 ? assetFromMatch : 0)}, template-match={(!keepSourceAssetWhenMissingInM26 ? assetFromMatch : 0)}, generic(missing)={assetFromGeneric}");
        res.Success = true;
        return res;
    }

    class DraftPlayer
    {
        public int Off, StatsStart, AssetStart, JsonStart, JsonEnd, Next;
        public string Fn, Ln;
    }

    static List<DraftPlayer> Parse(byte[] buf, bool isM26)
    {
        int off = isM26 ? 0x1046 : 0x1646;
        var list = new List<DraftPlayer>();
        while (off < buf.Length)
        {
            int fnEnd = Array.IndexOf(buf, (byte)0, off);
            if (fnEnd < 0) break;
            string fn = Encoding.ASCII.GetString(buf, off, fnEnd - off);
            if (string.IsNullOrEmpty(fn) || fn.Length > 20) break;
            int lnStart = off + (isM26 ? 17 : 21);
            int lnEnd = Array.IndexOf(buf, (byte)0, lnStart);
            if (lnEnd < 0) break;
            string ln = Encoding.ASCII.GetString(buf, lnStart, lnEnd - lnStart);
            int statsStart = isM26 ? off + 17 + 21 + 28 : off + 21 + 49;
            int assetStart = statsStart + (isM26 ? 92 : 90);
            if (assetStart + 32 > buf.Length) break;
            int jsonStart = Array.IndexOf(buf, (byte)'{', assetStart);
            if (jsonStart < 0) break;
            // find json end by brace counting
            int depth = 0, end = -1;
            for (int i = jsonStart; i < buf.Length; i++)
            {
                if (buf[i] == '{') depth++;
                else if (buf[i] == '}') depth--;
                if (depth == 0) { end = i + 1; break; }
                if (i - jsonStart > 8192) break;
            }
            if (end < 0) break;
            int next = end;
            while (next < buf.Length && buf[next] == 0) next++;
            list.Add(new DraftPlayer { Off = off, Fn = fn, Ln = ln, StatsStart = statsStart, AssetStart = assetStart, JsonStart = jsonStart, JsonEnd = end, Next = next });
            off = next;
            if (list.Count > 500) break;
        }
        return list;
    }
}
