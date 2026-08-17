namespace Attribution.Api.Controllers;

// FR-051: bound from the "NumberImport" configuration section. Points at a local or
// mounted folder an operator drops 8x8-exported number CSVs into for an administrator to
// trigger importing from the admin interface, as an alternative to a browser upload.
public sealed class NumberImportOptions
{
    public string FolderPath { get; set; } = string.Empty;
}
