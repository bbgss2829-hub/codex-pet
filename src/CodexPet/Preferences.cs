using System;
using System.IO;
using System.Text.Json;
namespace CodexPet;
public sealed class Preferences
{
    public double Left {get;set;}=100;
    public double Top {get;set;}=100;
    public double Scale {get;set;}=1;
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexPet");
    public static Preferences Load()
    { try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DirectoryPath,"settings.json")))??new(); } catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException) { return new(); } }
    public void Save()
    { try { Directory.CreateDirectory(DirectoryPath); File.WriteAllText(Path.Combine(DirectoryPath,"settings.json"),JsonSerializer.Serialize(this)); } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { } }
}
