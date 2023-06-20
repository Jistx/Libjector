using System.Runtime.InteropServices;

namespace Libjector.Models;

public record ProcessItemModel(int Id, string Name, Architecture? Architecture, string Path);