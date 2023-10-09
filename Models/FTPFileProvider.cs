using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Syncfusion.EJ2.FileManager.Base;

namespace Syncfusion.EJ2.FileManager.FTPFileProvider
{
    public class FTPFileProvider : IFTPFileProviderBase
    {
        protected string HostName;
        protected string RootPath;
        protected string RootName;
        protected string UserName;
        protected string Password;
        protected NetworkCredential Credentials = null;
        AccessDetails AccessDetails = new AccessDetails();

        public FTPFileProvider() { }

        public void SetFTPConnection(string ftpRootPath, string ftpUserName, string ftpPassword)
        {
            this.RootPath = ftpRootPath;
            this.UserName = ftpUserName;
            this.Password = ftpPassword;
            this.RootName = ftpRootPath.Split('/').Where(f => !string.IsNullOrEmpty(f)).LastOrDefault();
            FtpWebRequest request = this.CreateRequest(ftpRootPath);
            this.HostName = request.RequestUri.Host;
        }

        public void SetRules(AccessDetails details)
        {
            this.AccessDetails = details;
            DirectoryInfo root = new DirectoryInfo(this.RootPath);
            this.RootName = root.Name;
        }


        public FileManagerResponse GetFiles(string path, bool showHiddenItems, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse readResponse = new FileManagerResponse();
            try
            {
                this.RemoveTempImage();
                string fullPath = this.RootPath + path;
                fullPath = fullPath.Replace("../", "");

                FileManagerDirectoryContent cwd = new FileManagerDirectoryContent();
                FileManagerDirectoryContent cwdData = data.Length == 0 ? null : data[0];
                cwd = this.GetPathDetails(fullPath, path, cwdData);
                readResponse.CWD = cwd;
                if (cwd.Permission != null && !cwd.Permission.Read)
                {
                    readResponse.Files = null;
                    throw new UnauthorizedAccessException("'" + this.RootName + path + "' is not accessible. Access is denied.");
                }

                StreamReader reader = this.CreateReader(fullPath, WebRequestMethods.Ftp.ListDirectoryDetails);
                List<FileManagerDirectoryContent> items = new List<FileManagerDirectoryContent>();
                string line = reader.ReadLine();
                while (!String.IsNullOrEmpty(line))
                {
                    FileManagerDirectoryContent item = new FileManagerDirectoryContent();
                    FTPFileDetails detail = ParseDirectoryListLine(line);
                    bool isFile = detail.IsFile;
                    item.Name = detail.Name;
                    item.IsFile = isFile;
                    item.FilterPath = this.GetFilterPath(fullPath);
                    item.DateModified = detail.Modified;
                    item.Permission = GetPathPermission(fullPath);
                    if (isFile)
                    {
                        item.Size = detail.Size;
                        this.UpdateFileDetails(items, item, fullPath, detail.Name);
                    }
                    else
                    {
                        this.UpdateFolderDetails(items, item, fullPath, detail.Name);
                    }
                    line = reader.ReadLine();
                }
                reader.Close();
                readResponse.Files = items;
                return readResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("Access is denied") ? "401" : "417";
                readResponse.Error = er;
                return readResponse;
            }
        }

        public FileManagerResponse Create(string path, string name, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse createResponse = new FileManagerResponse();
            try
            {
                string fullPath = this.RootPath + path;
                fullPath = fullPath.Replace("../", "");
                string directoryPath = fullPath + name;
                try
                {
                    FtpWebRequest request = this.CreateRequest(directoryPath);
                    request.Method = WebRequestMethods.Ftp.MakeDirectory;
                    FtpWebResponse response = (FtpWebResponse)request.GetResponse();
                }
                catch (WebException e)
                {
                    ErrorDetails er = new ErrorDetails();
                    FtpWebResponse response = (FtpWebResponse)e.Response;
                    if (response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable && response.StatusDescription.Contains("exists"))
                    {
                        er.Code = "400";
                        er.Message = "A file or folder with the name " + name + " already exists.";
                    }
                    else
                    {
                        er.Code = "417";
                        er.Message = response.StatusDescription;
                    }
                    response.Close();
                    createResponse.Error = er;
                    return createResponse;
                }

                FileManagerDirectoryContent createData = new FileManagerDirectoryContent();
                createData.Name = name;
                createData.IsFile = false;
                createData.Size = 0;
                createData.DateModified = DateTime.Now;
                createData.DateCreated = createData.DateModified;
                createData.HasChild = false;
                createData.Type = "";
                FileManagerDirectoryContent[] newData = new FileManagerDirectoryContent[] { createData };
                createResponse.Files = newData;
                return createResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("Access is denied") ? "401" : "417";
                createResponse.Error = er;
                return createResponse;
            }
        }

        public FileManagerResponse Details(string path, string[] names, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse detailsResponse = new FileManagerResponse();
            FileDetails details = new FileDetails();
            try
            {
                if (names.Length == 0 || names.Length == 1)
                {
                    string fullPath = "";
                    if (names.Length == 0)
                    {
                        fullPath = (this.RootPath + path.Substring(0, path.Length - 1));
                        fullPath = fullPath.Replace("../", "");
                    }
                    else if (names[0] == null || names[0] == "")
                    {
                        fullPath = (this.RootPath + path);
                        fullPath = fullPath.Replace("../", "");
                    }
                    else
                    {
                        fullPath = Path.Combine(this.RootPath + path, names[0]);
                        fullPath = fullPath.Replace("../", "");
                    }
                    string[] fileDetails = this.SplitPath(fullPath, true);
                    bool isFile = data[0].IsFile;
                    details.Name = data[0].Name;
                    details.IsFile = isFile;
                    details.Size = this.ByteConversion(isFile ? data[0].Size : this.GetFolderSize(fullPath + "/", 0));
                    details.Modified = data[0].DateModified;
                    details.Created = details.Modified;
                    details.Location = this.RootName + ((data[0].FilterPath == "") ? "" : (data[0].FilterPath + data[0].Name));
                }
                else
                {
                    string previousPath = "";
                    string name = "";
                    long size = 0;
                    string location = "";
                    for (int i = 0; i < names.Length; i++)
                    {
                        string fullPath = "";
                        if (names[i] == null)
                        {
                            fullPath = this.RootPath + path;
                            fullPath = fullPath.Replace("../", "");
                        }
                        else
                        {
                            fullPath = (this.RootPath + path + names[i]);
                            fullPath = fullPath.Replace("../", "");
                        }
                        string[] fileDetails = this.SplitPath(fullPath, true);
                        bool isFile = data[i].IsFile;
                        name = (name == "") ? fileDetails[1] : (name + ", " + fileDetails[1]);
                        size += isFile ? data[i].Size : this.GetFolderSize(fullPath + "/", 0);
                        if ((previousPath == "" || previousPath == fileDetails[0]) && location != "Various Folders")
                        {
                            if (previousPath == "")
                            {
                                string[] splitPath = this.SplitPath(this.RootName + path + names[i], true);
                                int index = splitPath[0].LastIndexOf("/");
                                location = (index == -1) ? splitPath[0] : splitPath[0].Substring(0, index);
                            }
                            previousPath = fileDetails[0];
                        }
                        else
                        {
                            location = "Various Folders";
                        }
                    }
                    details.Name = name;
                    details.Size = this.ByteConversion(size);
                    details.Location = location;
                    details.MultipleFiles = true;
                }
                detailsResponse.Details = details;
                return detailsResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("Access is denied") ? "401" : "417";
                detailsResponse.Error = er;
                return detailsResponse;
            }
        }

        public FileManagerResponse Delete(string path, string[] names, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse deleteResponse = new FileManagerResponse();
            try
            {
                string basePath = this.RootPath + path;
                basePath = basePath.Replace("../", "");
                for (int i = 0; i < names.Length; i++)
                {
                    string fullPath = basePath + names[i];
                    string[] fileDetails = this.SplitPath(fullPath, true);
                    bool isFile = data[i].IsFile;
                }
                List<FileManagerDirectoryContent> items = new List<FileManagerDirectoryContent>();
                for (int i = 0; i < names.Length; i++)
                {
                    string fullPath = basePath + names[i];
                    string[] fileDetails = this.SplitPath(fullPath, true);
                    bool isFile = data[i].IsFile;
                    if (isFile)
                    {
                        this.DeleteFile(fullPath);
                    }
                    else
                    {
                        this.NestedDelete(fullPath);
                    }
                    items.Add(data[i]);
                }
                deleteResponse.Files = items;
                return deleteResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("Access is denied") ? "401" : "417";
                deleteResponse.Error = er;
                return deleteResponse;
            }
        }

        public FileManagerResponse Rename(string path, string name, string newName, bool replace = false, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse renameResponse = new FileManagerResponse();
            try
            {
                string basePath = this.RootPath + path;
                basePath = basePath.Replace("../", "");
                string fullPath = basePath + name;
                string newFullPath = basePath + newName;
                string oldName = fullPath.Split('/').Where(f => !string.IsNullOrEmpty(f)).LastOrDefault();
                string newFileName = newFullPath.Split('/').Where(f => !string.IsNullOrEmpty(f)).LastOrDefault();

                string[] fileDetails = this.SplitPath(fullPath, true);
                bool isFile = data[0].IsFile;
                List<FileManagerDirectoryContent> items = new List<FileManagerDirectoryContent>();
                string[] desDetails = this.SplitPath(newFullPath, true);
                if (this.IsExist(desDetails[0], desDetails[1], true))
                {
                    if (!oldName.Equals(newFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        ErrorDetails er = new ErrorDetails();
                        er.Code = "400";
                        er.Message = "Cannot rename " + oldName + " to " + newFileName + ": destination already exists.";
                        renameResponse.Error = er;
                        return renameResponse;
                    }
                    else
                    {
                        string tempNewFileName = "Sync_Temp_" + newFileName;
                        FtpWebRequest request = this.CreateRequest(fullPath);
                        request.Method = WebRequestMethods.Ftp.Rename;
                        request.RenameTo = tempNewFileName;
                        using (var response = (FtpWebResponse)request.GetResponse())
                        {
                            fullPath = basePath + tempNewFileName;
                        }
                    }
                }
                try
                {
                    FtpWebRequest request = this.CreateRequest(fullPath);
                    request.Method = WebRequestMethods.Ftp.Rename;
                    request.RenameTo = newFileName;
                    using (var response = (FtpWebResponse)request.GetResponse())
                    {
                        FileManagerDirectoryContent item = new FileManagerDirectoryContent();
                        item.Name = newFileName;
                        item.IsFile = isFile;
                        item.FilterPath = this.GetFilterPath(fileDetails[0]);
                        item.DateModified = DateTime.Now;
                        if (isFile)
                        {
                            item.Size = data[0].Size;
                            this.UpdateFileDetails(items, item, fileDetails[0], newFileName);
                        }
                        else
                        {
                            this.UpdateFolderDetails(items, item, fileDetails[0], newFileName);
                        }
                    }
                    renameResponse.Files = items;
                    return renameResponse;
                }
                catch (WebException e)
                {
                    ErrorDetails er = new ErrorDetails();
                    FtpWebResponse response = (FtpWebResponse)e.Response;
                    if (response.StatusDescription.Contains("exists"))
                    {
                        er.Code = "400";
                        er.Message = "Cannot rename " + oldName + " to " + newFileName + ": destination already exists.";
                    }
                    else
                    {
                        er.Code = "417";
                        er.Message = response.StatusDescription;
                    }
                    response.Close();
                    renameResponse.Error = er;
                    return renameResponse;
                }
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("Access is denied") ? "401" : "417";
                renameResponse.Error = er;
                return renameResponse;
            }
        }

        public FileManagerResponse Copy(string path, string targetPath, string[] names, string[] renameFiles, FileManagerDirectoryContent targetData, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse copyResponse = new FileManagerResponse();
            try
            {
                if (renameFiles == null)
                {
                    renameFiles = new string[0];
                }
                string desPath = this.RootPath + targetPath;
                desPath = desPath.Replace("../", "");
                List<string> existingFiles = new List<string>();
                List<string> missingFiles = new List<string>();
                List<FileManagerDirectoryContent> items = new List<FileManagerDirectoryContent>();
                string tempPath = this.RootPath + path;
                tempPath = tempPath.Replace("../", "");
                string srcPath = string.Empty;
                for (int i = 0; i < names.Length; i++)
                {
                    string fullName = names[i];
                    int name = names[i].LastIndexOf("/");
                    if (name >= 0)
                    {
                        srcPath = tempPath + names[i].Substring(0, name + 1);
                        names[i] = names[i].Substring(name + 1);
                    }
                    else
                    {
                        srcPath = tempPath;
                    }
                    FileManagerDirectoryContent item = new FileManagerDirectoryContent();
                    string srcName = srcPath + names[i];
                    string desName = desPath + names[i];
                    string[] srcDetails = this.SplitPath(srcName, true);
                    if (this.IsExist(srcDetails[0], srcDetails[1]))
                    {
                        string[] desDetails = this.SplitPath(desName, true);
                        bool isFile = data[i].IsFile;
                        if (isFile)
                        {
                            if (this.IsExist(desDetails[0], desDetails[1]))
                            {
                                int index = -1;
                                if (renameFiles.Length > 0)
                                {
                                    index = Array.FindIndex(renameFiles, row => row.Contains(names[i]));
                                }
                                if ((srcName == desName) || (index != -1))
                                {
                                    string newName = this.GetCopyName(desDetails[0], desDetails[1]);
                                    string newDesName = desPath + newName;
                                    this.CopyFileToServer(srcName, newDesName);
                                    string[] newDetails = this.SplitPath(newDesName, true);
                                    item = this.GetFileDetails(newDetails[0], newDetails[1], isFile, data[i].Size);
                                    item.PreviousName = names[i];
                                    items.Add(item);
                                }
                                else
                                {
                                    existingFiles.Add(names[i]);
                                }
                            }
                            else
                            {
                                this.CopyFileToServer(srcName, desName);
                                item = this.GetFileDetails(desDetails[0], desDetails[1], isFile, data[i].Size);
                                item.PreviousName = names[i];
                                items.Add(item);
                            }
                        }
                        else
                        {
                            if (this.IsExist(desDetails[0], desDetails[1]))
                            {
                                int index = -1;
                                if (renameFiles.Length > 0)
                                {
                                    index = Array.FindIndex(renameFiles, row => row.Contains(names[i]));
                                }
                                if ((srcName == desName) || (index != -1))
                                {
                                    string newName = this.GetCopyName(desDetails[0], desDetails[1]);
                                    string newDesName = desPath + newName;
                                    this.CopyDirectory(srcName, newDesName);
                                    string[] newDetails = this.SplitPath(newDesName, true);
                                    item = this.GetFileDetails(newDetails[0], newDetails[1], isFile);
                                    item.PreviousName = names[i];
                                    items.Add(item);
                                }
                                else
                                {
                                    existingFiles.Add(names[i]);
                                }
                            }
                            else
                            {
                                this.CopyDirectory(srcName, desName);
                                item = this.GetFileDetails(desDetails[0], desDetails[1], isFile);
                                item.PreviousName = names[i];
                                items.Add(item);
                            }
                        }
                    }
                    else
                    {
                        missingFiles.Add(names[i]);
                    }
                }
                copyResponse.Files = items;
                if (missingFiles.Count > 0)
                {
                    string namelist = missingFiles[0];
                    for (int k = 1; k < missingFiles.Count; k++)
                    {
                        namelist = namelist + ", " + missingFiles[k];
                    }
                    throw new FileNotFoundException(namelist + " not found in given location.");
                }
                if (existingFiles.Count > 0)
                {
                    ErrorDetails er = new ErrorDetails();
                    er.FileExists = existingFiles;
                    er.Code = "400";
                    er.Message = "File Already Exists";
                    copyResponse.Error = er;
                }
                return copyResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("Access is denied") ? "401" : "417";
                er.FileExists = copyResponse.Error?.FileExists;
                copyResponse.Error = er;
                return copyResponse;
            }
        }

        public FileManagerResponse Move(string path, string targetPath, string[] names, string[] renameFiles, FileManagerDirectoryContent targetData, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse moveResponse = new FileManagerResponse();
            try
            {
                if (renameFiles == null)
                {
                    renameFiles = new string[0];
                }
                string desPath = this.RootPath + targetPath;
                desPath = desPath.Replace("../", "");
                List<string> existingFiles = new List<string>();
                List<string> missingFiles = new List<string>();
                List<FileManagerDirectoryContent> items = new List<FileManagerDirectoryContent>();
                string tempPath = this.RootPath + path;
                tempPath = tempPath.Replace("../", "");
                string srcPath = string.Empty;
                for (int i = 0; i < names.Length; i++)
                {
                    string fullName = names[i];
                    int name = names[i].LastIndexOf("/");
                    if (name >= 0)
                    {
                        srcPath = tempPath + names[i].Substring(0, name + 1);
                        names[i] = names[i].Substring(name + 1);
                    }
                    else
                    {
                        srcPath = tempPath;
                    }
                    FileManagerDirectoryContent item = new FileManagerDirectoryContent();
                    string srcName = srcPath + names[i];
                    string desName = desPath + names[i];
                    string[] srcDetails = this.SplitPath(srcName, true);
                    if (this.IsExist(srcDetails[0], srcDetails[1]))
                    {
                        string[] desDetails = this.SplitPath(desName, true);
                        bool isFile = data[i].IsFile;
                        if (isFile)
                        {
                            if (this.IsExist(desDetails[0], desDetails[1]))
                            {
                                int index = -1;
                                if (renameFiles.Length > 0)
                                {
                                    index = Array.FindIndex(renameFiles, row => row.Contains(names[i]));
                                }
                                if ((srcName == desName) || (index != -1))
                                {
                                    string newName = this.GetCopyName(desDetails[0], desDetails[1]);
                                    string newDesName = desPath + newName;
                                    this.CopyFileToServer(srcName, newDesName);
                                    string[] newDetails = this.SplitPath(newDesName, true);
                                    item = this.GetFileDetails(newDetails[0], newDetails[1], isFile, data[i].Size);
                                    item.PreviousName = names[i];
                                    items.Add(item);
                                    this.DeleteFile(srcName);
                                }
                                else
                                {
                                    existingFiles.Add(names[i]);
                                }
                            }
                            else
                            {
                                this.CopyFileToServer(srcName, desName);
                                item = this.GetFileDetails(desDetails[0], desDetails[1], isFile, data[i].Size);
                                item.PreviousName = names[i];
                                items.Add(item);
                                this.DeleteFile(srcName);
                            }
                        }
                        else
                        {
                            if (this.IsExist(desDetails[0], desDetails[1]))
                            {
                                int index = -1;
                                if (renameFiles.Length > 0)
                                {
                                    index = Array.FindIndex(renameFiles, row => row.Contains(names[i]));
                                }
                                if ((srcName == desName) || (index != -1))
                                {
                                    string newName = this.GetCopyName(desDetails[0], desDetails[1]);
                                    string newDesName = desPath + newName;
                                    this.CopyDirectory(srcName, newDesName);
                                    string[] newDetails = this.SplitPath(newDesName, true);
                                    item = this.GetFileDetails(newDetails[0], newDetails[1], isFile);
                                    item.PreviousName = names[i];
                                    items.Add(item);
                                    this.NestedDelete(srcName);
                                }
                                else
                                {
                                    existingFiles.Add(names[i]);
                                }
                            }
                            else
                            {
                                this.CopyDirectory(srcName, desName);
                                item = this.GetFileDetails(desDetails[0], desDetails[1], isFile);
                                item.PreviousName = names[i];
                                items.Add(item);
                                this.NestedDelete(srcName);
                            }
                        }
                    }
                    else
                    {
                        missingFiles.Add(names[i]);
                    }
                }
                moveResponse.Files = items;
                if (missingFiles.Count > 0)
                {
                    string namelist = missingFiles[0];
                    for (int k = 1; k < missingFiles.Count; k++)
                    {
                        namelist = namelist + ", " + missingFiles[k];
                    }
                    throw new FileNotFoundException(namelist + " not found in given location.");
                }
                if (existingFiles.Count > 0)
                {
                    ErrorDetails er = new ErrorDetails();
                    er.FileExists = existingFiles;
                    er.Code = "400";
                    er.Message = "File Already Exists";
                    moveResponse.Error = er;
                }
                return moveResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("Access is denied") ? "401" : "417";
                er.FileExists = moveResponse.Error?.FileExists;
                moveResponse.Error = er;
                return moveResponse;
            }
        }

        public FileManagerResponse Search(string path, string searchString, bool showHiddenItems = false, bool caseSensitive = false, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse searchResponse = new FileManagerResponse();
            try
            {
                this.RemoveTempImage();
                string searchPath = this.RootPath + path;
                searchPath = searchPath.Replace("../", "");
                FileManagerDirectoryContent cwd = new FileManagerDirectoryContent();
                cwd = this.GetPathDetails(searchPath, path, data[0]);
                if (cwd.Permission != null && !cwd.Permission.Read)
                    throw new UnauthorizedAccessException("'" + this.RootName + path + "' is not accessible. Access is denied.");
                searchResponse.CWD = cwd;

                List<FileManagerDirectoryContent> foundedFiles = new List<FileManagerDirectoryContent>();
                this.NestedSearch(searchPath, foundedFiles, searchString, caseSensitive);
                searchResponse.Files = (IEnumerable<FileManagerDirectoryContent>)foundedFiles;
                return searchResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("Access is denied") ? "401" : "417";
                searchResponse.Error = er;
                return searchResponse;
            }
        }

        public FileStreamResult Download(string path, string[] names, params FileManagerDirectoryContent[] data)
        {
            try
            {
                string basePath = this.RootPath + path;
                basePath = basePath.Replace("../", "");
                int count = 0;
                for (int i = 0; i < names.Length; i++)
                {
                    string fullPath = basePath + names[i];
                    string[] fileDetails = this.SplitPath(fullPath, true);
                    bool isFile = data[i].IsFile;
                    if (isFile)
                    {
                        count++;
                    }
                }
                string tempPath = Path.Combine(Path.GetTempPath(), "temp.zip");
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
                string folderPath = Path.Combine(Path.GetTempPath(), "download_temp");
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                }
                Directory.CreateDirectory(folderPath);
                if (count == names.Length)
                {
                    if (count == 1)
                    {
                        string fullPath = basePath + names[0];
                        string[] details = this.SplitPath(fullPath, true);
                        FileStreamResult fileStreamResult = this.DownloadFile(fullPath, folderPath);
                        fileStreamResult.FileDownloadName = details[1];
                        return fileStreamResult;
                    }
                    else
                    {
                        string tempFileName;
                        ZipArchiveEntry zipEntry;
                        ZipArchive archive;
                        for (int i = 0; i < names.Count(); i++)
                        {
                            string fullPath = basePath + names[i];
                            using (archive = ZipFile.Open(tempPath, ZipArchiveMode.Update))
                            {
                                tempFileName = this.GetTempFilePath(fullPath, folderPath);
                                zipEntry = archive.CreateEntryFromFile(tempFileName, names[i], CompressionLevel.Fastest);
                            }
                        }
                        FileStream fileStreamInput = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                        FileStreamResult fileStreamResult = new FileStreamResult(fileStreamInput, "APPLICATION/octet-stream");
                        fileStreamResult.FileDownloadName = "files.zip";
                        return fileStreamResult;
                    }
                }
                else
                {
                    return DownloadFolder(path, names, tempPath, folderPath, data);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public FileManagerResponse Upload(string path, IList<IFormFile> uploadFiles, string action, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse uploadResponse = new FileManagerResponse();
            try
            {
                string fullPath = this.RootPath + path;
                fullPath = fullPath.Replace("../", "");
                List<string> existFiles = new List<string>();
                foreach (IFormFile file in uploadFiles)
                {
                    string name = file.FileName;
                    string fullName = fullPath + name;
                    if (action == "save")
                    {
                        if (!this.IsExist(fullPath, name))
                        {
                            this.UploadFile(file, fullName);
                        }
                        else
                        {
                            existFiles.Add(fullName);
                        }
                    }
                    else if (action == "replace")
                    {
                        if (this.IsExist(fullPath, name))
                        {
                            this.DeleteFile(fullName);
                        }
                        this.UploadFile(file, fullName);
                    }
                    else if (action == "keepboth")
                    {
                        string newName = this.GetCopyName(fullPath, name);
                        this.UploadFile(file, fullPath + newName);
                    }
                }
                if (existFiles.Count != 0)
                {
                    ErrorDetails er = new ErrorDetails();
                    er.Code = "400";
                    er.Message = "File already exists.";
                    er.FileExists = existFiles;
                    uploadResponse.Error = er;
                }
                return uploadResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Message = e.Message.ToString();
                er.Code = er.Message.Contains("Access is denied") ? "401" : "417";
                uploadResponse.Error = er;
                return uploadResponse;
            }
        }

        public FileStreamResult GetImage(string path, string id, bool allowCompress, ImageSize size, params FileManagerDirectoryContent[] data)
        {
            try
            {
                string fullPath = this.RootPath + path;
                fullPath = fullPath.Replace("../", "");
                string folderPath = Path.Combine(Path.GetTempPath(), "image_temp");
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
                return this.DownloadFile(fullPath, folderPath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public string ToCamelCase(FileManagerResponse userData)
        {
            return JsonConvert.SerializeObject(userData, new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            });
        }

        protected FtpWebRequest CreateRequest(string pathName)
        {
            FtpWebRequest result = (FtpWebRequest)FtpWebRequest.Create(pathName);
            if (this.Credentials == null)
                this.Credentials = new NetworkCredential(this.UserName, this.Password);
            result.Credentials = this.Credentials;
            return result;
        }

        protected FtpWebResponse CreateResponse(string fullPath, string method)
        {
            FtpWebRequest request = this.CreateRequest(fullPath);
            request.Method = method;
            return (FtpWebResponse)request.GetResponse();
        }

        protected StreamReader CreateReader(string fullPath, string method)
        {
            FtpWebResponse response = this.CreateResponse(fullPath, method);
            Stream responseStream = response.GetResponseStream();
            return new StreamReader(responseStream);
        }

        protected bool IsFile(string fullPath, string name)
        {
            StreamReader reader = this.CreateReader(fullPath, WebRequestMethods.Ftp.ListDirectory);
            bool isFile = false;
            int index = 0;
            string line = reader.ReadLine();
            while (!string.IsNullOrEmpty(line))
            {
                FTPFileDetails detail = ParseDirectoryListLine(line);
                index++;
                if (detail.Name == name)
                {
                    isFile = detail.IsFile;
                    break;
                }
                line = reader.ReadLine();
            }
            reader.Close();
            return isFile;
        }

        protected bool IsExist(string fullPath, string name, bool ignoreCase = false)
        {
            StreamReader reader = this.CreateReader(fullPath, WebRequestMethods.Ftp.ListDirectory);
            bool isExist = false;
            int index = 0;
            string line = reader.ReadLine();
            while (!string.IsNullOrEmpty(line))
            {
                index++;
                if (line == name || (ignoreCase && line.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    isExist = true;
                    break;
                }
                line = reader.ReadLine();
            }
            reader.Close();
            return isExist;
        }

        protected long GetFolderSize(string fileName, long size)
        {
            try
            {
                StreamReader reader = this.CreateReader(fileName, WebRequestMethods.Ftp.ListDirectoryDetails);
                string line = reader.ReadLine();

                while (!string.IsNullOrEmpty(line))
                {
                    FTPFileDetails detail = ParseDirectoryListLine(line);
                    bool isFile = detail.IsFile;
                    string fullPath = fileName + detail.Name;
                    if (isFile)
                    {
                        size += detail.Size;
                    }
                    else
                    {
                        size = this.GetFolderSize(fullPath + "/", size);
                    }
                    line = reader.ReadLine();
                }
                return size;
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        protected string GetFileExtension(string fileName)
        {
            int index = fileName.LastIndexOf(".");
            return (index == -1) ? "" : fileName.Substring(index);
        }

        protected string GetFileNameWithoutExtension(string fileName)
        {
            int index = fileName.LastIndexOf(".");
            return (index == -1) ? fileName : fileName.Substring(0, index);
        }

        protected bool HasChild(string path)
        {
            StreamReader reader = this.CreateReader(path, WebRequestMethods.Ftp.ListDirectoryDetails);
            bool hasChild = false;
            string[] list = reader.ReadToEnd().Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            reader.Close();
            for (int i = 0; i < list.Length; i++)
            {
                FTPFileDetails details = ParseDirectoryListLine(list[i]);
                hasChild = !details.IsFile;
                if (hasChild) { break; }
            }
            return hasChild;
        }

        protected string GetFilterPath(string path)
        {
            return path.Substring(this.RootPath.Length).Replace("/", "\\");
        }

        protected FileManagerDirectoryContent GetPathDetails(string fullPath, string path, FileManagerDirectoryContent data)
        {
            FileManagerDirectoryContent cwd = new FileManagerDirectoryContent();
            cwd.Name = fullPath.Split('/').Where(f => !string.IsNullOrEmpty(f)).LastOrDefault();
            cwd.IsFile = false;
            cwd.Size = 0;
            cwd.DateModified = data == null ? DateTime.Now : data.DateModified;
            cwd.DateCreated = cwd.DateModified;
            cwd.HasChild = this.HasChild(fullPath);
            cwd.Type = "";
            cwd.FilterPath = (path == "/") ? "" : this.GetFilterPath(this.SplitPath(fullPath)[0]);
            return cwd;
        }

        protected string[] SplitPath(string path, bool isFile = false)
        {
            string[] str_array = path.Split('/'), fileDetails = new string[2];
            string parentPath = "";
            int len = str_array.Length - (isFile ? 1 : 2);
            for (int i = 0; i < len; i++)
            {
                parentPath += str_array[i] + "/";
            }
            fileDetails[0] = parentPath;
            fileDetails[1] = str_array[len];
            return fileDetails;
        }

        protected string GetPath(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            return directory.FullName;
        }

        protected byte[] ConvertByte(Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream file = new MemoryStream())
            {
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    file.Write(buffer, 0, count);
                }
                return file.ToArray();
            }
        }

        protected string GetPattern(string pattern)
        {
            return "^" + Regex.Escape(pattern)
                              .Replace(@"\*", ".*")
                              .Replace(@"\?", ".")
                       + "$";
        }

        protected string ByteConversion(long fileSize)
        {
            try
            {
                string[] index = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
                if (fileSize == 0)
                {
                    return "0 " + index[0];
                }

                long bytes = Math.Abs(fileSize);
                int loc = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
                double num = Math.Round(bytes / Math.Pow(1024, loc), 1);
                return (Math.Sign(fileSize) * num).ToString() + " " + index[loc];
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        protected void NestedSearch(string searchPath, List<FileManagerDirectoryContent> foundedFiles, string searchString, bool caseSensitive)
        {
            try
            {
                StreamReader reader = this.CreateReader(searchPath, WebRequestMethods.Ftp.ListDirectoryDetails);
                int index = 0;
                string line = reader.ReadLine();
                while (!string.IsNullOrEmpty(line))
                {
                    FTPFileDetails detail = ParseDirectoryListLine(line);
                    index++;
                    bool matched = new Regex(this.GetPattern(searchString), (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)).IsMatch(detail.Name);
                    if (matched)
                    {
                        FileManagerDirectoryContent item = new FileManagerDirectoryContent();
                        item.Name = detail.Name;
                        item.IsFile = detail.IsFile;
                        item.FilterPath = this.GetFilterPath(searchPath);
                        if (detail.IsFile)
                        {
                            item.Size = detail.Size;
                            this.UpdateFileDetails(foundedFiles, item, searchPath, detail.Name);
                        }
                        else
                        {
                            string nestedPath = searchPath + detail.Name + "/";
                            item.DateModified = detail.Modified;
                            this.UpdateFolderDetails(foundedFiles, item, searchPath, detail.Name);

                            if (item.Permission == null || item.Permission.Read)
                            {
                                this.NestedSearch(nestedPath, foundedFiles, searchString, caseSensitive);
                            }
                        }
                    }
                    else
                    {
                        if (!detail.IsFile)
                        {
                            string nestedPath = searchPath + detail.Name + "/";

                            this.NestedSearch(nestedPath, foundedFiles, searchString, caseSensitive);
                        }
                    }
                    line = reader.ReadLine();
                }
                reader.Close();
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        protected void UpdateFileDetails(List<FileManagerDirectoryContent> items, FileManagerDirectoryContent item, string fullPath, string line)
        {
            item.DateCreated = item.DateModified;
            item.HasChild = false;
            item.Type = this.GetFileExtension(line);
            items.Add(item);
        }

        protected void UpdateFolderDetails(List<FileManagerDirectoryContent> items, FileManagerDirectoryContent item, string fullPath, string line)
        {
            string nestedPath = fullPath + line + "/";
            item.Size = 0;
            item.DateCreated = item.DateModified;
            item.HasChild = this.HasChild(nestedPath);
            item.Type = "";
            item.Permission = GetPathPermission(nestedPath);
            items.Add(item);
        }

        protected FileManagerDirectoryContent GetFileDetails(string fullPath, string name, bool isFile, long size = 0)
        {
            string nestedPath = fullPath + name + "/";
            FileManagerDirectoryContent item = new FileManagerDirectoryContent();
            item.Name = name;
            item.IsFile = isFile;
            item.Size = size;
            item.DateModified = DateTime.Now;
            item.DateCreated = item.DateModified;
            item.HasChild = isFile ? false : this.HasChild(nestedPath);
            item.Type = isFile ? this.GetFileExtension(name) : "";
            item.FilterPath = this.GetFilterPath(fullPath);
            return item;
        }

        protected void DeleteFile(string fullPath)
        {
            FtpWebResponse response = this.CreateResponse(fullPath, WebRequestMethods.Ftp.DeleteFile);
            response.Close();
        }

        protected void DeleteFolder(string fullPath)
        {
            FtpWebResponse response = this.CreateResponse(fullPath, WebRequestMethods.Ftp.RemoveDirectory);
            response.Close();
        }

        protected void NestedDelete(string folderName)
        {
            try
            {
                string fullPath = folderName + "/";
                StreamReader reader = this.CreateReader(fullPath, WebRequestMethods.Ftp.ListDirectoryDetails);
                string line = reader.ReadLine();
                while (!string.IsNullOrEmpty(line))
                {
                    FTPFileDetails detail = ParseDirectoryListLine(line);
                    bool isFile = detail.IsFile;
                    if (isFile)
                    {
                        this.DeleteFile(fullPath + detail.Name);
                    }
                    else
                    {
                        this.NestedDelete(fullPath + detail.Name);
                    }
                    line = reader.ReadLine();
                }
                if (string.IsNullOrEmpty(line))
                {
                    this.DeleteFolder(folderName);
                }
                reader.Close();
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        protected void UploadFile(IFormFile file, string fileName)
        {
            FtpWebRequest request = this.CreateRequest(fileName);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            using (Stream stream = request.GetRequestStream())
            {
                file.CopyTo(stream);
            }
        }

        protected FileStreamResult DownloadFile(string fullPath, string folderPath)
        {
            string tempPath = this.GetTempFilePath(fullPath, folderPath);
            FileStream fileStreamInput = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
            FileStreamResult fileStreamResult = new FileStreamResult(fileStreamInput, "APPLICATION/octet-stream");
            return fileStreamResult;
        }

        protected string GetTempFilePath(string fullPath, string folderPath)
        {
            string tempPath = Path.Combine(folderPath, DateTime.Now.ToFileTime().ToString()) + fullPath.Split('/').Last();
            this.CopyFile(fullPath, tempPath);
            return tempPath;
        }

        protected void CopyFile(string fileName, string tempPath)
        {
            FtpWebResponse response = this.CreateResponse(fileName, WebRequestMethods.Ftp.DownloadFile);
            byte[] buffer = this.ConvertByte(response.GetResponseStream());
            using (Stream file = File.OpenWrite(tempPath))
            {
                file.Write(buffer, 0, buffer.Length);
            }
            response.Close();
        }

        protected void CopyFileToServer(string fileName, string tempPath)
        {
            FtpWebResponse response = this.CreateResponse(fileName, WebRequestMethods.Ftp.DownloadFile);
            byte[] buffer = this.ConvertByte(response.GetResponseStream());
            FtpWebRequest request = this.CreateRequest(tempPath);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(buffer, 0, buffer.Length);
            }
            response.Close();
        }

        protected FileStreamResult DownloadFolder(string path, string[] names, string tempPath, string folderPath, FileManagerDirectoryContent[] data)
        {
            string basePath = this.RootPath + path;
            basePath = basePath.Replace("../", "");
            List<string> fileList = new List<string>();
            List<string> folderList = new List<string>();
            string fileName;
            ZipArchiveEntry zipEntry;
            ZipArchive archive;
            for (int i = 0; i < names.Count(); i++)
            {
                this.UpdateDownloadPath(folderPath, basePath, names[i], fileList, folderList, data[i]);
            }
            using (archive = ZipFile.Open(tempPath, ZipArchiveMode.Update))
            {
                for (int j = 0; j < folderList.Count; j++)
                {
                    fileName = folderList[j].Substring(folderPath.Length + 1);
                    zipEntry = archive.CreateEntry(fileName);
                }
                for (int j = 0; j < fileList.Count; j++)
                {
                    fileName = fileList[j].Substring(folderPath.Length + 1);
                    zipEntry = archive.CreateEntryFromFile(fileList[j], fileName, CompressionLevel.Fastest);
                }
            }
            FileStream fileStreamInput = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Delete);
            FileStreamResult fileStreamResult = new FileStreamResult(fileStreamInput, "APPLICATION/octet-stream");
            if (names.Length == 1)
            {
                fileStreamResult.FileDownloadName = data[0].Name + ".zip";
            }
            else
            {
                fileStreamResult.FileDownloadName = "folders.zip";
            }
            return fileStreamResult;
        }

        protected void UpdateDownloadPath(string folderPath, string basePath, string folderName, List<string> fileList, List<string> folderList, FileManagerDirectoryContent data)
        {
            string fullPath = basePath + folderName;
            string newFolderName = folderName.Replace("/", "\\");
            string[] fileDetails = this.SplitPath(fullPath, true);
            bool isFile = data.IsFile;
            if (isFile)
            {
                string path = Path.Combine(folderPath, newFolderName.Substring(0, newFolderName.Length - fileDetails[1].Length));
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                string tempPath = Path.Combine(folderPath, newFolderName);
                if (!fileList.Contains(tempPath))
                {
                    this.CopyFile(fullPath, tempPath);
                    fileList.Add(tempPath);
                }
            }
            else
            {
                string path = Path.Combine(folderPath, newFolderName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                string validPath = fullPath + "/";
                StreamReader reader = this.CreateReader(validPath, WebRequestMethods.Ftp.ListDirectoryDetails);
                int index = 0;
                string line = reader.ReadLine();
                while (!string.IsNullOrEmpty(line))
                {
                    FTPFileDetails detail = ParseDirectoryListLine(line);
                    index++;
                    bool isSubFile = detail.IsFile;
                    if (isSubFile)
                    {
                        string fileName = validPath + detail.Name;
                        string tempPath = Path.Combine(folderPath, newFolderName, detail.Name);
                        if (!fileList.Contains(tempPath))
                        {
                            this.CopyFile(fileName, tempPath);
                            fileList.Add(tempPath);
                        }
                    }
                    else
                    {
                        FileManagerDirectoryContent item = new FileManagerDirectoryContent();
                        item.IsFile = false;
                        this.UpdateDownloadPath(folderPath, basePath, folderName + "/" + detail.Name, fileList, folderList, item);
                    }
                    line = reader.ReadLine();
                }
                if (index == 0) { folderList.Add(path + "\\"); }
            }
        }

        protected void RemoveTempImage()
        {
            string folderPath = Path.Combine(Path.GetTempPath(), "image_temp");
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, true);
            }
            Directory.CreateDirectory(folderPath);
        }

        protected string GetCopyName(string fullPath, string name)
        {
            string newName = name;
            int index = newName.LastIndexOf(".");
            if (index >= 0)
                newName = newName.Substring(0, index);
            int fileCount = 0;
            string extn = this.GetFileExtension(name);
            while (this.IsExist(fullPath, newName + (fileCount > 0 ? "(" + fileCount.ToString() + ")" : "") + extn))
            {
                fileCount++;
            }
            newName = newName + (fileCount > 0 ? "(" + fileCount.ToString() + ")" : "") + extn;
            return newName;
        }

        protected void CopyDirectory(string srcName, string desName)
        {
            FtpWebRequest request = this.CreateRequest(desName);
            request.Method = WebRequestMethods.Ftp.MakeDirectory;
            FtpWebResponse response = (FtpWebResponse)request.GetResponse();
            string srcPath = srcName + "/";
            string desPath = desName + "/";
            StreamReader reader = this.CreateReader(srcPath, WebRequestMethods.Ftp.ListDirectoryDetails);
            string line = reader.ReadLine();
            while (!string.IsNullOrEmpty(line))
            {
                FTPFileDetails detail = ParseDirectoryListLine(line);
                string srcFileName = srcPath + detail.Name;
                string desFileName = desPath + detail.Name;
                bool isSubFile = detail.IsFile;
                if (isSubFile)
                {
                    this.CopyFileToServer(srcFileName, desFileName);
                }
                else
                {
                    this.CopyDirectory(srcFileName, desFileName);
                }
                line = reader.ReadLine();
            }
            reader.Close();
        }

        private FTPFileDetails ParseDirectoryListLine(string line)
        {
            FTPFileDetails details = new FTPFileDetails();
            string unixRegex = @"^(?<DIR>[-d])((?:[-r][-w][-xs]){3})\s+\d+\s+\w+(?:\s+\w+)?\s+(?<FileSize>\d+)\s+(?<Modified>\w+\s+\d+(?:\s+\d+(?::\d+)?))\s+(?!(?:\.|\.\.)\s*$)(?<FileName>.+?)\s*$";
            string msDosRegex = @"^(?<Modified>\d{2}\-\d{2}\-(\d{2,4})\s+\d{2}:\d{2}[Aa|Pp][mM])\s+(?<DIR>\<\w+\>){0,1}(?<FileSize>\d+){0,1}\s+(?<FileName>.+)";
            Match parsedLine;
            if (Regex.IsMatch(line, unixRegex))
            {
                parsedLine = Regex.Match(line, unixRegex);
                details.IsFile = parsedLine.Groups["DIR"].Value != "d";
            }
            else if (Regex.IsMatch(line, msDosRegex))
            {
                parsedLine = Regex.Match(line, msDosRegex);
                details.IsFile = parsedLine.Groups["DIR"].Value != "<DIR>";
            }
            else
            {
                throw new Exception("Non implemented response format");
            }
            details.Modified = Convert.ToDateTime(parsedLine.Groups["Modified"].Value);
            details.Size = details.IsFile ? Convert.ToInt64(parsedLine.Groups["FileSize"].Value) : 0;
            details.Name = parsedLine.Groups["FileName"].Value;
            return details;
        }
        private class FTPFileDetails
        {
            public bool IsFile { get; set; }

            public DateTime Modified { get; set; }

            public string Name { get; set; }

            public long Size { get; set; }
        }

        protected virtual AccessPermission GetPathPermission(string path)
        {
            string[] fileDetails = GetFolderDetails(path);
            return GetPermission(GetPath(fileDetails[0]), fileDetails[1], false);
        }

        protected virtual string[] GetFolderDetails(string path)
        {
            string[] str_array = path.Split('/'), fileDetails = new string[2];
            string parentPath = "";
            for (int i = 0; i < str_array.Length - 2; i++)
            {
                parentPath += str_array[i] + "/";
            }
            fileDetails[0] = parentPath;
            fileDetails[1] = str_array[str_array.Length - 2];
            return fileDetails;
        }

        protected virtual string GetFilePath(string path)
        {
            return Path.GetDirectoryName(path) + Path.DirectorySeparatorChar;

        }

        protected virtual bool HasPermission(Permission rule)
        {
            return rule == Permission.Allow ? true : false;
        }

        protected virtual AccessPermission UpdateFileRules(AccessPermission filePermission, AccessRule fileRule)
        {
            filePermission.Copy = HasPermission(fileRule.Copy);
            filePermission.Download = HasPermission(fileRule.Download);
            filePermission.Write = HasPermission(fileRule.Write);
            filePermission.Read = HasPermission(fileRule.Read);
            filePermission.Message = string.IsNullOrEmpty(fileRule.Message) ? string.Empty : fileRule.Message;
            return filePermission;
        }
        protected virtual AccessPermission UpdateFolderRules(AccessPermission folderPermission, AccessRule folderRule)
        {
            folderPermission.Copy = HasPermission(folderRule.Copy);
            folderPermission.Download = HasPermission(folderRule.Download);
            folderPermission.Write = HasPermission(folderRule.Write);
            folderPermission.WriteContents = HasPermission(folderRule.WriteContents);
            folderPermission.Read = HasPermission(folderRule.Read);
            folderPermission.Upload = HasPermission(folderRule.Upload);
            folderPermission.Message = string.IsNullOrEmpty(folderRule.Message) ? string.Empty : folderRule.Message;
            return folderPermission;
        }

        protected virtual string GetValidPath(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            return directory.FullName;
        }

        protected virtual AccessPermission GetPermission(string location, string name, bool isFile)
        {
            AccessPermission FilePermission = new AccessPermission();
            if (isFile)
            {
                if (this.AccessDetails.AccessRules == null) return null;
                string nameExtension = Path.GetExtension(name).ToLower();
                string fileName = Path.GetFileNameWithoutExtension(name);
                string currentPath = GetFilePath(location + name);
                foreach (AccessRule fileRule in AccessDetails.AccessRules)
                {
                    if (!string.IsNullOrEmpty(fileRule.Path) && fileRule.IsFile && (fileRule.Role == null || fileRule.Role == AccessDetails.Role))
                    {
                        if (fileRule.Path.IndexOf("*.*") > -1)
                        {
                            string parentPath = fileRule.Path.Substring(0, fileRule.Path.IndexOf("*.*"));
                            if (currentPath.IndexOf(GetPath(parentPath)) == 0 || parentPath == "")
                            {
                                FilePermission = UpdateFileRules(FilePermission, fileRule);
                            }
                        }
                        else if (fileRule.Path.IndexOf("*.") > -1)
                        {
                            string pathExtension = Path.GetExtension(fileRule.Path).ToLower();
                            string parentPath = fileRule.Path.Substring(0, fileRule.Path.IndexOf("*."));
                            if ((GetPath(parentPath) == currentPath || parentPath == "") && nameExtension == pathExtension)
                            {
                                FilePermission = UpdateFileRules(FilePermission, fileRule);
                            }
                        }
                        else if (fileRule.Path.IndexOf(".*") > -1)
                        {
                            string pathName = Path.GetFileNameWithoutExtension(fileRule.Path);
                            string parentPath = fileRule.Path.Substring(0, fileRule.Path.IndexOf(pathName + ".*"));
                            if ((GetPath(parentPath) == currentPath || parentPath == "") && fileName == pathName)
                            {
                                FilePermission = UpdateFileRules(FilePermission, fileRule);
                            }
                        }
                        else if (GetPath(fileRule.Path) == GetValidPath(location + name))
                        {
                            FilePermission = UpdateFileRules(FilePermission, fileRule);
                        }
                    }
                }
                return FilePermission;
            }
            else
            {
                if (this.AccessDetails.AccessRules == null) { return null; }
                foreach (AccessRule folderRule in AccessDetails.AccessRules)
                {
                    if (folderRule.Path != null && folderRule.IsFile == false && (folderRule.Role == null || folderRule.Role == AccessDetails.Role))
                    {
                        if (folderRule.Path.IndexOf("*") > -1)
                        {
                            string parentPath = folderRule.Path.Substring(0, folderRule.Path.IndexOf("*"));
                            if (GetValidPath(location + name).IndexOf(GetPath(parentPath)) == 0 || parentPath == "")
                            {
                                FilePermission = UpdateFolderRules(FilePermission, folderRule);
                            }
                        }
                        else if (GetPath(this.RootPath + folderRule.Path) == GetValidPath(location + name) || GetPath(folderRule.Path) == GetValidPath(location + name + Path.DirectorySeparatorChar))
                        {
                            FilePermission = UpdateFolderRules(FilePermission, folderRule);
                        }
                        else if (GetValidPath(location + name).IndexOf(GetPath(folderRule.Path)) == 0)
                        {
                            FilePermission.Write = HasPermission(folderRule.WriteContents);
                            FilePermission.WriteContents = HasPermission(folderRule.WriteContents);
                        }
                    }
                }
                return FilePermission;
            }
        }
    }
}
