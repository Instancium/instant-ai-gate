
## 🔒 Security Notes

By default, the development and quick-start configurations are optimized for instant deployment. However, before exposing **InstantAIGate** to any production environment or network, you must secure your installation.

### ⚠️ Critical: Change the Default API Keys in Docker Compose

In the default `docker-compose.yml` file, the administration API key is set to `"skip"` for both the API backend and the Admin UI dashboard. 

> 🚨 **WARNING:** Leaving the API key as `"skip"` completely disables authentication. Anyone with network access 
    to your containers will be able to manage your models, view metrics, and access the core infrastructure. 
    **Never use `"skip"` in production.**

#### How to Properly Secure Docker Compose

The `APIClientOptions__AdminApiKey` in the `admin` service **must perfectly match** the `ApiKeyOptions__AdminKey` in the `api` service.

You can secure your deployment using one of two methods:

##### Method A: Using an Environment File (`.env`) — *Recommended*
1. Create a `.env` file in the same directory as your `docker-compose.yml`.
2. Generate a secure random string (e.g., using a password generator or `openssl rand -hex 32`).
3. Add the key to your `.env` file:
   ```env
   INSTANT_AI_GATE_SECRET_KEY=your_super_secure_random_api_key_here
   ```

4.Update your docker-compose.yml to reference this variable:
   
   ```yaml
   # Inside the 'api' service environment variables:
	- ApiKeyOptions__AdminKey=${INSTANT_AI_GATE_SECRET_KEY}

	# Inside the 'admin' service environment variables:
	- APIClientOptions__AdminApiKey=${INSTANT_AI_GATE_SECRET_KEY}
   ```

##### Method B: Direct Definition
Alternatively, you can hardcode the identical matching key directly into the compose file:
   ```yaml
    services:
      api:
        # ...
        environment:
          - ApiKeyOptions__AdminKey=9f82c3d5a4b1e6... # Your secure key

      admin:
        # ...
        environment:
          - APIClientOptions__AdminApiKey=9f82c3d5a4b1e6... # Must match the key above exactly
   ```

### 🛡️ Windows Native Security (How it works)

If you used the **Windows Native Installation Script** (`install.ps1`), your setup is **secure by default**. 

The PowerShell installer automatically generates a unique, cryptographically secure 64-character GUID pair behind the scenes 
and injects it into both `appsettings.json` files during deployment. No manual action is required for Windows service deployments 
unless you wish to rotate the keys manually.