Got it. Here is the English version of the README.md and the configuration example.

# InstantAIGate.Infrastructure.ModelManagement

This infrastructure module is responsible for secure path resolution, downloading, and synchronizing AI model files from external storages (HuggingFace, S3). The module implements a strict whitelisting model and supports smart download resumption.

## Core Components

* **FileStorageService**: Handles secure operations with the local file system.


* Downloads files to temporary paths with a `.tmp` suffix.


* Ensures cache integrity via atomic directory moves (`MoveDirectoryAtomic`) only after all files are successfully downloaded.



* **ModelSynchronizer**: Orchestrates the streaming download process of artifacts into the base `models_cache` directory.


* Skips downloading a file if it already exists locally and its size matches the remote size.


* Yields aggregated download status (`AggregateDownloadProgress`) in real-time.




* **HuggingFaceResolver**: Integrates with the HuggingFace API.


* Recursively traverses the repository's folder structure to find all required model files.


* Uses the `HuggingFaceItem` DTO to properly deserialize JSON responses from the API.


* Automatically applies the `ApiToken` for models with the `ModelTier.APIUsing` access tier.




* **S3Resolver**: A lightweight client for S3-compatible storages.


* Sends HTTP GET requests with the `list-type=2` parameter and parses XML responses to extract file keys and sizes.




* **SupportedModelsDictionary**: A built-in catalog (whitelist) that defines the models permitted for use.


* Contains specific builds (variants), such as `cuda` and `cpu` for the `Qwen/Qwen3-VL-4B-Instruct-ONNX` model, pointing to the exact target directory paths in the remote repository.




* **ModelConfigurationService**: A service for reading user settings.


* Retrieves the array of active models from the `ActiveModels` section and authorization parameters from the `Providers` section.





---

## Configuration Example (appsettings.json)

Below is an example configuration to activate a supported model and set up provider authorization, which is read by the `ModelConfigurationService`.

```json
{
  "ActiveModels": [
    "Qwen/Qwen3-VL-4B-Instruct-ONNX"
  ],
  "Providers": {
    "HuggingFace": {
      "ApiToken": "hf_your_personal_access_token_here"
    },
    "S3Private": {
      "Endpoint": "https://s3.your-private-storage.com",
      "Bucket": "ai-models-bucket",
      "ApiToken": "your_s3_authorization_bearer_token"
    }
  }
}

```