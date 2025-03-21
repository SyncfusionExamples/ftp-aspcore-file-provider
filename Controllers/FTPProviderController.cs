using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Collections.Generic;
using Syncfusion.EJ2.FileManager.Base;
using Syncfusion.EJ2.FileManager.FTPFileProvider;
using System.Text.Json;

namespace EJ2APIServices.Controllers
{

    [Route("api/[controller]")]
    [EnableCors("AllowAllOrigins")]
    public class FTPProviderController : Controller
    {
        public FTPFileProvider operation;
        public FTPProviderController(IWebHostEnvironment hostingEnvironment)
        {
            this.operation = new FTPFileProvider();
            //Specify the FTP hostname, username and password
            this.operation.SetFTPConnection("ftp://xxx.xx.xxx.xxx/", "xxxxxx", "xxxxx");
        }
        [Route("FTPFileOperations")]
        public object FTPFileOperations([FromBody] FileManagerDirectoryContent args)
        {
            switch (args.Action)
            {
                case "read":
                    return this.operation.ToCamelCase(this.operation.GetFiles(args.Path, args.ShowHiddenItems, args.Data));
                case "delete":
                    return this.operation.ToCamelCase(this.operation.Delete(args.Path, args.Names, args.Data));
                case "copy":
                    return this.operation.ToCamelCase(this.operation.Copy(args.Path, args.TargetPath, args.Names, args.RenameFiles, args.TargetData, args.Data));
                case "move":
                    return this.operation.ToCamelCase(this.operation.Move(args.Path, args.TargetPath, args.Names, args.RenameFiles, args.TargetData, args.Data));
                case "details":
                    return this.operation.ToCamelCase(this.operation.Details(args.Path, args.Names, args.Data));
                case "create":
                    return this.operation.ToCamelCase(this.operation.Create(args.Path, args.Name, args.Data));
                case "search":
                    return this.operation.ToCamelCase(this.operation.Search(args.Path, args.SearchString, args.ShowHiddenItems, args.CaseSensitive, args.Data));
                case "rename":
                    return this.operation.ToCamelCase(this.operation.Rename(args.Path, args.Name, args.NewName,false, args.Data));
            }
            return null;
        }

        [Route("FTPUpload")]
        public IActionResult FTPUpload(string path, IList<IFormFile> uploadFiles, string action)
        {
            FileManagerResponse uploadResponse;
            uploadResponse = operation.Upload(path, uploadFiles, action, null);
            if (uploadResponse.Error != null)
            {
               Response.Clear();
               Response.ContentType = "application/json; charset=utf-8";
               Response.StatusCode = Convert.ToInt32(uploadResponse.Error.Code);
               Response.HttpContext.Features.Get<IHttpResponseFeature>().ReasonPhrase = uploadResponse.Error.Message;
            }
            return Content("");
        }

        [Route("FTPDownload")]
        public IActionResult FTPDownload(string downloadInput)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            FileManagerDirectoryContent args = JsonSerializer.Deserialize<FileManagerDirectoryContent>(downloadInput, options);
            return operation.Download(args.Path, args.Names,args.Data);
        }

        [Route("FTPGetImage")]
        public IActionResult FTPGetImage(FileManagerDirectoryContent args)
        {
            return this.operation.GetImage(args.Path, args.Id, true, null, args.Data);
        }
    }

}
