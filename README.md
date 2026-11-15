# OpenAI Competitive

An OpenAI-compatible translation plugin for STranslate with custom provider names and /v1/models model discovery.

## Features

- Support custom vendor name, convenient to distinguish different services in STranslate's service list and translation floating window
- Support custom API URL and API Key
- Support manual model name input
- Support fetching model list via `/v1/models` using current API URL and API Key
- After fetching models successfully, can directly select from dropdown the models returned by the server
- Retain STranslate's existing prompt editing, temperature control and connection validation capabilities

## Usage

After adding the service, on the plugin settings page fill in:

- **Vendor Name**: Used to display in STranslate service list and translation floating window
- **API URL**: OpenAI or compatible service's interface address
- **API Key**: Key required to access the service
- **Model**: Can be manually input, or click "Fetch Models" from `/v1/models`

If the service provider's base URL is:

```
https://api.example.com
```

The plugin will request models at:

```
https://api.example.com/v1/models
```

## Installation

1. Download the `.spkg` file from Release
2. Open STranslate
3. Go to the plugin management page
4. Select the local `.spkg` file to install
5. Restart or refresh the plugin, then in translation services add "OpenAI Competitive"

## Supported Models

All OpenAI-compatible models