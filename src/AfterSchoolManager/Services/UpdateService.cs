using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AfterSchoolManager.Models;

namespace AfterSchoolManager.Services;

public sealed class UpdateService
{
    private static readonly HttpClient Client=CreateClient();

    public async Task<UpdateInfoItem> CheckAsync(string repository,CancellationToken cancellationToken=default)
    {
        var (owner,name)=ParseRepository(repository);
        using var response=await Client.GetAsync($"https://api.github.com/repos/{owner}/{name}/releases/latest",cancellationToken);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"GitHub Release를 확인하지 못했습니다. HTTP {(int)response.StatusCode}");
        await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);using var json=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);var root=json.RootElement;
        var tag=root.GetProperty("tag_name").GetString()??"0.0.0";var releaseName=root.TryGetProperty("name",out var title)?title.GetString()??tag:tag;
        var page=root.GetProperty("html_url").GetString()??"";string? installerName=null,installerUrl=null,installerSha256=null;
        if(root.TryGetProperty("assets",out var assets))foreach(var asset in assets.EnumerateArray())
        {
            var candidate=asset.GetProperty("name").GetString();
            if(candidate?.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)==true)
            {
                installerName=candidate;installerUrl=asset.GetProperty("browser_download_url").GetString();
                if(asset.TryGetProperty("digest",out var digest)){var raw=digest.GetString();if(raw?.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)==true)installerSha256=raw[7..];}
                break;
            }
        }
        var current=Assembly.GetExecutingAssembly().GetName().Version??new Version(0,0,0);
        var latest=ParseVersion(tag);return new UpdateInfoItem{CurrentVersion=Normalize(current),LatestVersion=Normalize(latest),ReleaseName=releaseName,ReleasePageUrl=page,InstallerName=installerName,InstallerUrl=installerUrl,InstallerSha256=installerSha256,IsUpdateAvailable=latest.CompareTo(current)>0};
    }

    public async Task<string> DownloadInstallerAsync(UpdateInfoItem update,string destinationDirectory,IProgress<int>? progress=null,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(update.InstallerUrl)||string.IsNullOrWhiteSpace(update.InstallerName))throw new InvalidOperationException("최신 Release에 Windows 설치파일(.exe)이 없습니다.");
        var uri=new Uri(update.InstallerUrl);if(uri.Scheme!=Uri.UriSchemeHttps||!(uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".githubusercontent.com",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("허용되지 않은 업데이트 다운로드 주소입니다.");
        Directory.CreateDirectory(destinationDirectory);var path=Path.Combine(destinationDirectory,Path.GetFileName(update.InstallerName));
        using var response=await Client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellationToken);response.EnsureSuccessStatusCode();
        var total=response.Content.Headers.ContentLength;
        {
            await using var input=await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None,81920,true);
            var buffer=new byte[81920];long received=0;int read;while((read=await input.ReadAsync(buffer,cancellationToken))>0){await output.WriteAsync(buffer.AsMemory(0,read),cancellationToken);received+=read;if(total>0)progress?.Report((int)(received*100/total.Value));}
        }
        if(!string.IsNullOrWhiteSpace(update.InstallerSha256))
        {
            await using var file=File.OpenRead(path);var hash=Convert.ToHexString(await SHA256.HashDataAsync(file,cancellationToken));
            if(!hash.Equals(update.InstallerSha256,StringComparison.OrdinalIgnoreCase)){File.Delete(path);throw new InvalidDataException("설치파일 SHA-256 무결성 검증에 실패했습니다.");}
        }
        progress?.Report(100);return path;
    }

    private static HttpClient CreateClient(){var client=new HttpClient{Timeout=TimeSpan.FromMinutes(5)};client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AfterSchoolIntegratedManager","0.5"));client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));return client;}
    private static (string Owner,string Name) ParseRepository(string value)
    {
        var raw=(value??"").Trim().TrimEnd('/');if(Uri.TryCreate(raw,UriKind.Absolute,out var uri)&&uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase))raw=uri.AbsolutePath.Trim('/');
        var parts=raw.Split('/',StringSplitOptions.RemoveEmptyEntries);if(parts.Length!=2||parts.Any(x=>x.Any(ch=>!(char.IsLetterOrDigit(ch)||ch is '-' or '_' or '.'))))throw new ArgumentException("GitHub 저장소를 owner/repository 형식으로 입력하세요.");
        return(parts[0],parts[1]);
    }
    private static Version ParseVersion(string value){var normalized=value.Trim().TrimStart('v','V').Split('-')[0];return Version.TryParse(normalized,out var version)?version:throw new InvalidDataException("Release 버전을 해석할 수 없습니다: "+value);}
    private static string Normalize(Version version)=>$"{version.Major}.{version.Minor}.{Math.Max(0,version.Build)}";
}
