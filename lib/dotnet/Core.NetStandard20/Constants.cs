﻿// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.SemanticMemory.Core20;

public static class Constants
{
    // Default User ID owning documents uploaded without specifying a user
    public const string DefaultDocumentOwnerUserId = "defaultUser";

    // Form field containing the User ID
    public const string WebServiceUserIdField = "userId";

    // Form field containing the Document ID
    public const string WebServiceDocumentIdField = "documentId";

    // Internal file used to track progress of asynchronous pipelines
    public const string PipelineStatusFilename = "__pipeline_status.json";

    // Tags reserved for internal logic
    public const string ReservedUserIdTag = "__user";
    public const string ReservedDocIdTag = "__doc_id";
    public const string ReservedFileIdTag = "__file_id";
    public const string ReservedFilePartitionTag = "__file_part";
    public const string ReservedFileTypeTag = "__file_type";
}