# ASP.NET Core FTP service for the file manager component

This repository contains the ASP.NET Core file transfer protocol file system providers for the Essential JS 2 File Manager component.

## Key Features

A file system provider is an API for access to the hosted file system using File Transfer Protocol(**FTP**) in the FileManager control. It also provides the methods for performing various file actions like creating a new folder, renaming files and deleting files.

ASP.NET Core FTP file system provider serves the FTP file system for the file manager component.

The following actions can be performed with ASP.NET Core file system Provider.

| **Actions** | **Description** |
| --- | --- |
| Read      | Reads the files from the FTP file storage. |
| Details   | Gets a file's metadata which consists of Type, Size, Location and Modified date. |
| Download  | Downloads the selected file or folder. |
| Upload    | Upload's the file in hosted file system. It accepts uploaded media with the following characteristics: <ul><li>Maximum file size:  30MB</li><li>Accepted Media MIME types: `*/*` </li></ul> |
| Create    | Creates a new folder. |
| Delete    | Deletes a folder or file. |
| Copy      | Copies the contents of the file from the target location . |
| Move      | Paste the copied files to the desired location. |
| Rename    | Renames a folder or file. |
| Search    | Searches a file or folder. |


## Prerequisites

To run the service, open the `FTPFileProvider` and register the FTP  details like *hostName*, *userName*, *password* details in the `SetFTPConnection` method to perform the file operations. 

> Provide the *hostName* parameter as like root path in the `SetFTPConnection` method.

```
   void SetFTPConnection(string hostName, string userName, string password);   
```

## How to run this application?

To run this application, you need to first clone the `ej2-ftp-aspcore-file-provider` repository and then navigate to its appropriate path where it has been located in your system.

To do so, open the command prompt and run the below commands one after the other.

```
git clone https://github.com/SyncfusionExamples/ej2-ftp-aspcore-file-provider  ej2-ftp-aspcore-file-provider

cd ej2-ftp-aspcore-file-provider

```

## Running application

Once cloned, open the solution file in visual studio.Then build the project, after restoring the nuget packages and run it.

## File Manager AjaxSettings

To access the basic actions such as Read, Delete, Copy, Move, Rename, Search, and Get Details of File Manager using FTP service, just map the following code snippet in the **AjaxSettings** property of File Manager.

Here, the `hostUrl` will be your locally hosted port number.

```
  var hostUrl = http://localhost:62870/;
  ajaxSettings: {
        url: hostUrl + 'api/FTPProvider/FTPFileOperations'
  }
```

## File download AjaxSettings

To perform download operation, initialize the `downloadUrl` property in ajaxSettings of the File Manager component.

```
  var hostUrl = http://localhost:62870/;
  ajaxSettings: {
        url: hostUrl + 'api/FTPProvider/FTPFileOperations',
        downloadUrl: hostUrl +'api/FTPProvider/FTPDownload'
  }
```

## File upload AjaxSettings

To perform upload operation, initialize the `uploadUrl` property in ajaxSettings of the File Manager component.

```
  var hostUrl = http://localhost:62870/;
  ajaxSettings: {
        url: hostUrl + 'api/FTPProvider/FTPFileOperations',
        uploadUrl: hostUrl +'api/FTPProvider/FTPUpload'
  }
```

## File image preview AjaxSettings

To perform image preview support in the File Manager component, initialize the `getImageUrl` property in ajaxSettings of the File Manager component.

```
  var hostUrl = http://localhost:62870/;
  ajaxSettings: {
        url: hostUrl + 'api/FTPProvider/FTPFileOperations',
         getImageUrl: hostUrl +'api/FTPProvider/FTPGetImage'
  }
```

The FileManager will be rendered as the following.

![File Manager](https://ej2.syncfusion.com/products/images/file-manager/readme.gif)

## Support

Product support is available for through following mediums.

* Creating incident in Syncfusion [Direct-trac](https://www.syncfusion.com/support/directtrac/incidents?utm_source=npm&utm_campaign=filemanager) support system or [Community forum](https://www.syncfusion.com/forums/essential-js2?utm_source=npm&utm_campaign=filemanager).
* New [GitHub issue](https://github.com/syncfusion/ej2-javascript-ui-controls/issues/new).
* Ask your query in [Stack Overflow](https://stackoverflow.com/?utm_source=npm&utm_campaign=filemanager) with tag `syncfusion` and `ej2`.

## License

Check the license detail [here](https://github.com/syncfusion/ej2-javascript-ui-controls/blob/master/license).

## Changelog

Check the changelog [here](https://github.com/syncfusion/ej2-javascript-ui-controls/blob/master/controls/filemanager/CHANGELOG.md)

© Copyright 2020 Syncfusion, Inc. All Rights Reserved. The Syncfusion Essential Studio license and copyright applies to this distribution.
