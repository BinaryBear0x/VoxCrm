# VoxCRM - Veterinary & Clinic Management System

VoxCRM is a comprehensive, multi-tenant Customer Relationship Management (CRM) solution specifically designed for veterinary clinics and clinic networks (dealers). It centralizes patient (pet) and owner management, appointment scheduling, financial tracking, and automated communication.

## Key Features

* **Multi-Tenant Architecture:** Ensures complete data isolation between different clinics using Entity Framework Core Global Query Filters and `ITenantEntity`.
* **Flexible Record Management:** Patient and owner records are designed with maximum flexibility. Fields are largely nullable, removing strict data-entry barriers during rapid clinic registrations.
* **Automated WhatsApp Integration:** A dedicated microservice (Gateway) handles outbound messages. The system queues messages (e.g., vaccination reminders, manual texts) into the database, which are then processed asynchronously via polling to ensure no UI blocking or performance degradation.
* **Financial & Appointment Tracking:** Built-in modules for managing daily cash flows, income/expense tracking, and scheduling clinic appointments.
* **Robust Security Mechanisms:** Implements strict Data Binding constraints and model state validations to prevent Mass Assignment and IDOR (Insecure Direct Object Reference) vulnerabilities.

## Technology Stack

* **Backend Framework:** C# / .NET 8, ASP.NET Core MVC (Client/Dealer Portals), ASP.NET Core Web API (External Services).
* **Database:** PostgreSQL (with Entity Framework Core Code-First Migrations).
* **Background Processing:** Hangfire (used for scheduling recurring tasks like vaccination reminders).
* **Frontend UI:** Razor Pages, Bootstrap 5, jQuery. Includes real-time character counters and toast notification systems.
* **External Gateway:** Python/Node.js-based independent WhatsApp Gateway.

## Directory Structure

* `/VoxCrm.Domain/`: Core domain entities, constant definitions, and interfaces (e.g., `ITenantEntity`).
* `/VoxCrm.Infrastructure/`: EF Core `DbContext`, Database Migrations, and Background Jobs (Hangfire) implementations.
* `/VoxCrm.Web/`: The primary web application (MVC), containing Controllers, Razor Views, and Security/Audit implementations.
* `/VoxCrm.Api/`: Exposes RESTful endpoints for external service integrations.

## Architecture & Design Decisions

### 1. Flexible Data Entry (Nullable Fields)
Veterinary clinics often need to create records quickly with partial information (e.g., just a phone number or pet name). To accommodate this, most properties on `PetOwner` and `Patient` entities are strictly nullable. UI validations are handled softly via warning modals rather than hard database blocks.

### 2. Asynchronous Messaging (Database Polling)
Direct HTTP calls to the WhatsApp Gateway for messaging were avoided to prevent UI hangs in case of gateway downtime. Instead, messages are inserted into the `WhatsAppNotifications` table with a `Pending` status. The Gateway periodically polls this table and dispatches the messages, ensuring high availability and retry capabilities.

## Troubleshooting & Known Issues (Errors & Solutions)

During the development and scaling of VoxCRM, several key challenges were addressed:

* **Mass Assignment & IDOR Risks on Edit Operations:**
  * **Issue:** Entities could be improperly modified if unexpected form fields were injected in POST requests.
  * **Solution:** Applied strict `[Bind("Id, Field1, Field2")]` attributes to all POST controllers. Additionally, explicit `ModelState.Remove()` was utilized for non-form fields (like Tenant ID or navigation properties) to ensure clean validations without breaking the tenant isolation.
* **Turkish Character Encoding Issues:**
  * **Issue:** Razor views and toast notifications occasionally rendered Turkish characters improperly.
  * **Solution:** Global encoding setups were verified and localized Razor strings were sanitized to ensure complete UTF-8 compliance across the UI.
* **Gateway `404 Not Found` on Direct Sends:**
  * **Issue:** Attempting to send messages directly via `/api/clinics/{id}/whatsapp/send` resulted in a 404 from the Gateway.
  * **Solution:** Migrated the logic from direct HTTP API calls to the Database Polling architecture (`WhatsAppNotifications.Add`). The system now natively queues manual messages alongside automated reminders using anonymous temporary `PetOwner` mappings when required.
* **Entity Framework Migration Anomalies:**
  * **Issue:** Missing tools or context reference errors during PostgreSQL schema updates.
  * **Solution:** Migrations were restructured to ensure `[DbContext]` and `[Migration]` attributes were properly generated. Context factory implementations were fortified for design-time generation.

## Getting Started

1. Ensure **PostgreSQL** and **.NET 8 SDK** are installed.
2. Clone the repository and navigate to the project root.
3. Update connection strings in `appsettings.Development.json` (Local PostgreSQL).
4. Apply database migrations: `dotnet ef database update --project VoxCrm.Infrastructure --startup-project VoxCrm.Web`
5. Run the web application: `dotnet run --project VoxCrm.Web`
