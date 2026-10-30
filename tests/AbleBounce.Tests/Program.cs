using System.IO;
using System.Text;
using AbleBounce;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
void Reject(Action action, string message)
{ try { action(); } catch (IOException) { checks++; return; } throw new Exception(message); }
string temp = Path.Combine(Path.GetTempPath(), "AbleBounce-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string Wav(int bits, int format = 1, int channels = 2, int frames = 100)
    {
        var path = Path.Combine(temp, Guid.NewGuid() + ".wav");
        using var f = File.Create(path); using var w = new BinaryWriter(f, Encoding.ASCII);
        int block = channels * bits / 8, data = frames * block;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((ushort)format); w.Write((ushort)channels); w.Write(48000); w.Write(48000 * block);
        w.Write((ushort)block); w.Write((ushort)bits); w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data); w.Write(new byte[data]);
        return path;
    }
    foreach (int bits in new[] { 16, 24, 32 })
    {
        var wave = WaveFile.Inspect(Wav(bits, bits == 32 ? 3 : 1), bits);
        Check(wave.Frames == 100 && wave.SampleRate == 48000 && wave.Bits == bits, "Valid WAV rejected");
    }
    Reject(() => WaveFile.Inspect(Wav(16), 24), "Wrong bit depth accepted");
    Reject(() => WaveFile.Inspect(Wav(32, 1), 32), "Integer 32-bit accepted instead of float");
    Reject(() => WaveFile.Inspect(Wav(16, channels: 1), 16), "Unexpected mono accepted");
    Reject(() => WaveFile.Inspect(Wav(16, frames: 0), 16), "Empty render accepted");
    var truncated = Wav(24); using (var f = File.OpenWrite(truncated)) f.SetLength(47);
    Reject(() => WaveFile.Inspect(truncated, 24), "Truncated render accepted");
    var unfinished = Wav(16); using (var f = File.OpenWrite(unfinished)) { f.Position = 4; f.Write(new byte[4]); }
    Reject(() => WaveFile.Inspect(unfinished, 16), "Unfinished RIFF accepted");
    var safe = Storage.SafeName("A<>:\"/\\|?*Song. ");
    Check(safe.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !safe.EndsWith('.'), "Unsafe export filename");
    Check(Storage.SafeName(new string('x', 200)).Length <= 90, "Export filename was not bounded");
    Check(DeliveryRow.Create().Select(d => d.Id).SequenceEqual(Enumerable.Range(1, 9)), "Nine delivery IDs changed");
    Console.WriteLine($"PASS: {checks} desktop core regression checks");
}
finally { Directory.Delete(temp, true); }
