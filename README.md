# ItemIconDownloader
Export item icons from the Lodestone DB.

## Requirements
* .NET 6.0 SDK
* Local game installation

## Usage
Navigate into the folder with the `.csproj` file and run the following command:
```bat
dotnet run -- all -s <path to your game's sqpack folder> -o <path to the desired output directory>
```

To only export marketable item icons, replace the `all` parameter with `marketable`.
