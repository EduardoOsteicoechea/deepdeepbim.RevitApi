using System.Text.Json;
using Amazon;
using Amazon.S3.Model;

namespace deepdeepbim.RevitApi.ApplicationUpdater;

public interface IApplicationConfigurationFilesUpdater
{
	Task UpdateLessThan1000AppFilesFromFlatS3BucketAsync(string targetDirectoryPath);
	List<string> DowloadedDlls { get; set; }
}

public class ApplicationConfigurationFilesUpdater : IApplicationConfigurationFilesUpdater
{
	S3LocalLoadableCredentialsModel _credentials { get; set; }
	S3Service _s3Service { get; set; }
	public List<string> DowloadedFiles { get; set; }
	public List<string> DowloadedDlls { get; set; }
	public ApplicationConfigurationFilesUpdater(string credentialsFilePath)
	{
		_credentials = new S3LocalLoadableCredentialsLoaderService(credentialsFilePath).Credentials;
		_s3Service = new S3Service(_credentials.AccessKey, _credentials.SecretKey, _credentials.AwsRegion);
	}
	public async Task UpdateLessThan1000AppFilesFromFlatS3BucketAsync(string targetDirectoryPath)
	{
		DowloadedFiles = await _s3Service.DownloadLessThan1000AppFilesFromFlatS3BucketAsync(_credentials.BucketName, targetDirectoryPath);
		DowloadedDlls = DowloadedFiles.Where(a => a.Contains(".dll")).ToList();
	}
}

internal class S3LocalLoadableCredentialsModel
{
	public string AccessKey { get; set; }
	public string SecretKey { get; set; }
	public string AwsRegion { get; set; }
	public string BucketName { get; set; }
}

internal class S3LocalLoadableCredentialsLoaderService
{
	public S3LocalLoadableCredentialsModel Credentials { get; init; }
	public S3LocalLoadableCredentialsLoaderService(string credentialsFilePath)
	{
		try
		{
			string content = File.ReadAllText(credentialsFilePath);

			var options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};

			Credentials = System.Text.Json.JsonSerializer.Deserialize<S3LocalLoadableCredentialsModel>(content, options);
		}
		catch (System.Exception ex)
		{
			throw new Exception($"Failed to load S3 credentials from {credentialsFilePath}: {ex.Message}", ex);
		}
	}
}

internal class S3Service
{
	private readonly string _accessKey;
	private readonly string _secretKey;
	private readonly RegionEndpoint _regionEndpoint;

	public S3Service(string accessKey, string secretKey, string regionSystemName)
	{
		_accessKey = accessKey;
		_secretKey = secretKey;
		_regionEndpoint = RegionEndpoint.GetBySystemName(regionSystemName);
	}

	public async Task<List<string>> DownloadLessThan1000AppFilesFromFlatS3BucketAsync
	(
		string bucketName,
		string targetFolderPath
	)
	{
		using var client = new Amazon.S3.AmazonS3Client(_accessKey, _secretKey, _regionEndpoint);

		if (!Directory.Exists(targetFolderPath))
		{
			Directory.CreateDirectory(targetFolderPath);
		}

		ListObjectsV2Request listRequest = new ListObjectsV2Request
		{
			BucketName = bucketName
		};

		ListObjectsV2Response listResponse = await client.ListObjectsV2Async(listRequest);

		List<S3Object> filesToDownload = listResponse.S3Objects.Where(a =>
				a.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
					||
				a.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
			)
			.ToList();

		var result = new List<string>();

		int filesToDownloadCount = filesToDownload.Count;

		for (int i = 0; i < filesToDownloadCount; i++)
		{
			S3Object s3Object = filesToDownload[i];

			string fileName = Path.GetFileName(s3Object.Key);

			if (string.IsNullOrEmpty(fileName)) continue;

			string localFilePath = Path.Combine(targetFolderPath, fileName);

			var downloadedFilePath =  await DownloadSingleFileAsync(client, bucketName, s3Object.Key, localFilePath);

			result.Add(downloadedFilePath);
		}

		return result;
	}

	private async Task<string> DownloadSingleFileAsync
	(
		Amazon.S3.AmazonS3Client client,
		string bucketName,
		string objectKey,
		string localFilePath
	)
	{
		try
		{
			GetObjectRequest getRequest = new GetObjectRequest
			{
				BucketName = bucketName,
				Key = objectKey
			};

			using (var response = await client.GetObjectAsync(getRequest))
			using (var responseStream = response.ResponseStream)
			using (var fileStream = File.Create(localFilePath))
			{
				await responseStream.CopyToAsync(fileStream);
			}

			return localFilePath;
		}
		catch (Exception ex)
		{
			throw new Exception($"Failed to download {objectKey}: {ex.Message}", ex);
		}
	}
}