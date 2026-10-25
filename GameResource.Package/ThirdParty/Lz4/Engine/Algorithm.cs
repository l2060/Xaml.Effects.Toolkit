// Vendored from K4os.Compression.LZ4 1.3.8; see README.md and LICENSE.*.txt in this directory.
#nullable disable

namespace GameResource.Package.Internal.Lz4.Engine;

/// <summary>Algorithm selection.</summary>
internal enum Algorithm
{
	/// <summary>Intel and ARMv7 version of 32 bit algorithm.</summary>
	X32,

	/// <summary>Intel version of 64 bit algorithm.</summary>
	X64,
}