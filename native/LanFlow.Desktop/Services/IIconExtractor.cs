using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace LanFlow.Desktop.Services;

public interface IIconExtractor
{
    ValueTask<ImageSource?> ExtractAsync(
        string path,
        int pixelSize,
        CancellationToken cancellationToken);
}
