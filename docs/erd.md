# Contoso Service Intake ERD

```mermaid
erDiagram
    CONTACT ||--o{ CONTOSO_SERVICE_REQUEST : submits
    CONTOSO_SERVICE_CATEGORY ||--o{ CONTOSO_SERVICE_REQUEST : categorizes
    CONTOSO_SERVICE_CATEGORY ||--o{ CONTOSO_ROUTING_SLA_RULE : filters
    CONTOSO_ROUTING_SLA_RULE ||--o{ CONTOSO_SERVICE_REQUEST : matched_by
    TEAM ||--o{ CONTOSO_ROUTING_SLA_RULE : routes_to
    TEAM ||--o{ CONTOSO_SERVICE_REQUEST : owns
    SYSTEM_USER ||--o{ CONTOSO_ROUTING_SLA_RULE : approves
    SYSTEM_USER ||--o{ CONTOSO_SERVICE_REQUEST : approved_by
    CONTOSO_SERVICE_REQUEST ||--o{ ANNOTATION : has_supporting_documents
    CONTOSO_SERVICE_REQUEST ||--o{ CONTOSO_ERROR_LOG : logs

    CONTACT {
        guid contactid PK
        string firstname
        string lastname
        string emailaddress1
        string address1_telephone1
        string address1_line1
        string address1_city
        string address1_stateorprovince
        string address1_postalcode
        string address1_country
    }

    CONTOSO_SERVICE_CATEGORY {
        guid contoso_servicecategoryid PK
        string contoso_name
    }

    CONTOSO_ROUTING_SLA_RULE {
        guid contoso_routingslaruleid PK
        string contoso_name
        lookup contoso_categoryid FK
        choice contoso_severity
        lookup contoso_routedteamid FK
        int contoso_slahours
        bool contoso_approvalrequired
        lookup contoso_approverid FK
        bool contoso_externalsyncrequired
        int contoso_priority
        bool contoso_active
        memo contoso_portalmessage
    }

    TEAM {
        guid teamid PK
        string name
        choice teamtype
    }

    SYSTEM_USER {
        guid systemuserid PK
        string fullname
        string internalemailaddress
    }

    CONTOSO_SERVICE_REQUEST {
        guid contoso_servicerequestid PK
        string contoso_title
        autonumber contoso_confirmationnumber
        string contoso_requesteremail
        lookup contoso_requestercontactid FK
        lookup contoso_categoryid FK
        choice contoso_severity
        memo contoso_description
        choice contoso_status
        lookup contoso_routedteamid FK
        lookup ownerid FK
        lookup contoso_routingslaruleid FK
        int contoso_slahours
        datetime contoso_responsedueon
        datetime contoso_resolutiondueon
        choice contoso_approvalstatus
        lookup contoso_approvedby FK
        datetime contoso_approvedon
        choice contoso_externalsyncstatus
        string contoso_externalsystemid
        datetime contoso_externalsyncedon
        memo contoso_resolutionsummary
        memo contoso_internalresolutionnotes
    }

    ANNOTATION {
        guid annotationid PK
        lookup objectid FK
        string filename
        string mimetype
        memo notetext
        binary documentbody
    }

    CONTOSO_ERROR_LOG {
        guid contoso_errorlogid PK
        string contoso_name
        choice contoso_source
        string contoso_operation
        lookup contoso_servicerequestid FK
        memo contoso_message
        memo contoso_details
        bool contoso_retryable
        string contoso_correlationid
        string contoso_flowrunid
        choice contoso_status
    }
```

## Relationship Notes

- `Service Request` is user/team owned and is assigned to the routed owner team by the plugin.
- `Routed Team` is retained as an audit/reporting lookup even if `ownerid` is later reassigned.
- `Routing/SLA Rule` is organization owned so administrators can maintain the routing matrix without creating per-user ownership complexity.
- `Annotation` stores portal-uploaded supporting documents through Power Pages notes/timeline configuration.
- `Integration/Error Log` stores plugin, flow, portal, and external API failures for operational review.
