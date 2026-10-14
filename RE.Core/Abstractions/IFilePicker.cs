using System.Threading.Tasks;

namespace RE.Core.Abstractions
{
    public interface IFilePicker
    {
        Task<string?> PickFileAsync();
    }
}