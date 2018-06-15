# Brand Identifier

This application identifies custom brands in video

## Setup

1. Setup your Video Indexer account. Go to [videoindexer.ai](https://videoindexer.ai) and log in with your corporate 

2. Setup Custom Vision AI account.

3. Upload your images and press train.

4. Deploy the infrastructure components:
    <a href="https://ms.portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fswgriffith%2FBrandIdentifier%2Fmaster%2Fazure-deploy.json" target="_blank">
        <img src="http://azuredeploy.net/deploybutton.png"/>
    </a>


5. Download the latest FunctionApp zip file and WebApp zip file from the releases folder in this repo.
6. Navigate to the FunctionApp code deploy page: https://<funtion_name>.scm.azurewebsites.net/ZipDeploy
7. Drag and drop the FunctionApp zip file into the file area on the ZipDeploy page.
8. Create a raw container in the storage account
9. Update Logic App with Function App URL.