# Brand Identifier

This application identifies custom brands in video

## Setup

1. Deploy the infrastructure components:
    <a href="https://ms.portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fjohndehavilland%2FBrandIdentifier%2Fmaster%2Fazure-deploy.json" target="_blank">
        <img src="http://azuredeploy.net/deploybutton.png"/>
    </a>

2. Download the latest FunctionApp zip file and WebApp zip file from the releases folder in this repo.
3. Navigate to the FunctionApp code deploy page: https://<funtion_name>.scm.azurewebsites.net/ZipDeploy
4. Drag and drop the FunctionApp zip file into the file area on the ZipDeploy page.
5. Navigate to the VideoIndexer connector
6. Update the connection properties and add in the VideoIndexer API Key and press save.