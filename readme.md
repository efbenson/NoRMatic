# NOTE: This is an alpha library, please use with caution.

# NoRMatic
NoRMatic is a wrapper/extender for the excellent [NoRM](http://www.normproject.com/) library (thanks to Andrew Theken) for interacting with MongoDB in .NET.  NoRM exposes a complete LINQ provider over MongoDB collections along with helpers for index and collection management as well as strongly typed document mapping.  NoRMatic applies an easy-to-use API in an ActiveRecord style using NoRM as it's underlying data access layer.  NoRMatic also provides behavior hooks for before and after save, before and after delete, as well as soft deleting, versioning, and basic auditing.

## Model Basics (NoRMaticModel<T>)
Most of what you'll need to use NoRMatic is provided in the NoRMaticModel<T> base class.  Any class inheriting from this will be able to take advantage of the following members:

### Instance Members

#### Properties
* Id [ObjectId] - The MongoDB _id property for the document
* DateUpdated [DateTime] - This is updated each time the document is saved
* Errors [List<ValidationResult>] - A list of validation results from System.ComponentModel.DataAnnotations
* IsDeleted [bool] - If EnableSoftDelete is set, this will indicate if the document has been deleted
* DateDeleted [DateTime] - If EnableSoftDelete is set, this indicates the date the document was deleted
* IsVersion [bool] - If EnableVersioning is set, this will indicate if the document is a version
* DateVersioned [DateTime] - If EnableVersioning is set, this indicates the date the version was created
* VersionOfId [ObjectId] - If EnableVersioning is set, this is the source document's Id

#### Methods
* Save() [void] - Persists the document and creates versions if EnableVersioning is set
* Delete() [void] - Deletes the document or "soft deletes" it if EnableSoftDelete is set
* GetVersions() [List<T>] - Returns a list of versions for the document if EnableVersioning is set

### Static Methods

* All() [IEnumerable<T>] - Returns all documents from the collection excluding versions or soft deleted documents
* Find(Expression<Func<T, bool>>) [IQueryable<T>] - Finds a document via the NoRM LINQ provider
* FindOne(Expression<Func<T, bool>>) [T] - Finds a single document via the NoRM LINQ provider
* Exists(Expression<Func<T, bool>>) [bool] - Returns a boolean indicating whether a matching document exists
* GetById(ObjectId) [T] - Finds a single document by its Id which includes soft deleted items
* DeleteAll() [void] - Drops the entire collection regardless of EnableSoftDelete
* GetMongoCollection() [IMongoCollection<T>] - Returns a raw hook to the NoRM collection

Here is a basic example of using the NoRMaticModel<T> base class:

	public class Customer : NoRMaticModel<Customer> { ... }
	var customer = new Customer { Name = "Bill Smith", Address = "101 Address Way" };
	customer.Save();
	customer.Delete();

## Soft Deletes
To enable soft delete on a type, use the static method EnableSoftDelete() on any NoRMaticModel<T> type.  For example, the following will enable soft delete for all instances of Customer or Product.
	
	Customer.EnableSoftDelete();
	Product.EnableSoftDelete();

When soft delete is enabled, any calls to Delete() will not remove the document from the collection, but will set the documents IsDeleted property to true and the DateDeleted property to the current date.  The document will no longer be updateable, you will be able to access it, but not save any changes to the documents properties.

## Versioning
To enable versioning on a type, use the static method EnableVersioning() on any NoRMaticModel<T> type.  For example, the following will enable versioning for all instances of Note and MedicalRecord.
	
	Note.EnableVersioning();
	MedicalRecord.EnableVersioning();

Versions are stored in the same collection as the source documents except they have three additional properties set: IsVersion, DateVersioned, and VersionOfId.  The version documents recieve a new Id but their VersionOfId property points back to the Id of the source document.  Each instance of a versioned document has a GetVersions() method wich will return all past versions of the document.

## Validation
Validation in NoRMatic is done using the System.ComponentModel.DataAnnotations attributes.  If any of these validation attributes are applied, all calls to Save() will cause validation to occur.  Any documents with validation errors will not be saved.  The documents Errors property can be used to view any errors generated by the current state of the document.

The base set of validation attributes doesn't support complex nested types (such as a produc having a list of sub-products) so NoRMatic provides the [ValidateChild].  This attribute, when applied to a property, causes deep validation for the document.  The ValidateChild attribute also supports enumerables of a complex type (like in the example below).

	public class OrderItem {
		[Required]
		public string Sku { get; set; }
		
		[Required]
		public int Quantity { get; set; }
	}

	public class Order : NoRMaticModel<Order> {
		
		[Required]
		public string CustomerName { get; set; }
		
		[Required, DataType(DataType.EmailAddress)]
		public string CustomerEmail { get; set; }
		
		[ValidateChild]
		public List<OrderItem> Items { get; set; }		
	}
	
	var order = new Order { ... };
	order.Errors // list of any errors based on above validation attributes
	order.Save(); // would only persist the document if no errors exist

## User Auditing
User auditing simply means that when a document is saved, the current user is looked up and some value is set for the UpdatedBy property of any NoRMaticModel<T>.  Because the framework doesn't have any concept of the current user, the user must be retrieved.  To facilitate this two things are required to get user auditing setup.

	//Register the current user provider (simply an anonymous function)
	NoRMaticConfig.SetCurrentUserProvider(() => Session["UserId"]);
	
	//EnableUserAuditing for the target type.
	Subscriber.EnableUserAuditing();
	
Each time a Subscriber is saved, the current user provider function will be executed which will return (in this case) the UserId value from Session...you could however source this data from anything which is in scope for the provider function.

## Behaviors
Behaviors is a way to add universal functionality to a collection without needing to override any base types as well as to simulate event hooks at certain points in CRUD operations.  There are five types of behaviors that can be added to a type:

* Query
* BeforeSave
* AfterSave
* BeforeDelete
* AfterDelete

Behaviors are basically just anonymous functions which are executed before or after an operation.  In the case of Query behaviors, the function is an expression which is added to the query to be executed (used in Find() and All() methods).  Behaviors are registered through static methods on any type which inherits from NoRMaticModel<T>.
	
### Query Behaviors

	NoRMaticModel<T>.AddQueryBehavior(Expression<Func<T, bool>>>) [void]

In the example below, the Find() expression will be combined with the query behavior expression and will limit results to those documents with an Inventory greater than 0 and a Supplier of "ACME".  Query behaviors can be used to apply universal limiting to queries in multi-tennant or user security situations.

	Product.AddQueryBehavior(x => x.Inventory > 0); // Configuration
	var products = Product.Find(x => x.Supplier == "ACME"); // Usage

### BeforeSave Behaviors

	NoRMaticModel<T>.AddBeforeSaveBehavior(Func<T, bool>) [void]

In the example below, any calls to Save() will execute the anonymous function registered as BeforeSave behaviors for the given type.  Notice that the function returns a boolean, if any BeforeSave behavior functions return false the save will be aborted.  Among other uses, this allows for a handy just-in-time validation hook.

	Product.AddBeforeSaveBehavior(x => x.AccountId == CurrentAccountId); // Configuration
	var product = new Product { ... }
	product.Save(); // Executes any registered before save behaviors

### AfterSave Behaviors

	NoRMaticModel<T>.AddAfterSaveBehavior(Action<T>) [void]
	
AfterSave behaviors are simply actions that are executed immediately after any call to Save() and after a version is created if EnableVersioning is set.

### BeforeDelete Behaviors

	NoRMaticModel<T>.AddBeforeDeleteBehavior(Func<T, bool>) [void]

In the example below, any calls to Delete() will execute the anonymous function registered as BeforeDelete behaviors for the given type.  Notice that the function returns a boolean, like the BeforeSave behaviors returning false from any of the registered BeforeDelete behaviors will abort the delete action.

	Product.AddBeforeDeleteBehavior(x => x.AccountId == CurrentAccountId); //Configuration
	var product = new Product { ... };
	product.Delete(); // Execute any registered before delete behaviors

### AfterDelete Behaviors

	NoRMaticModel<T>.AddAfterDeleteBehavior(Action<T>) [void]

AfterDelete behaviors are simply actions that are executed immediately after any call to Delete().

### Abstract Behaviors

In addition to the behaviors that can be added to individual model types, the before/after save and before/after delete behaviors can be added to abstract types or interfaces and will affect any NoRMaticModel<T> entities that inherit from those abstract types.  For example:
	
	public interface IMustHaveAnAccountId {
		int AccountId { get; set;}
	}
	
	public class Customer : NoRMaticModel<Customer>, IMustHaveAnAccountId {
		public int AccountId { get; set; }
		...
	}
	
	NoRMaticConfig.AddBeforeSaveAbstractBehavior<IMustHaveAnAccountId>(x => { x.AccountId = 123; return true; });
	
In the above example, the abstract behavior will run when Save() is called for any type which inherits NoRMaticModel<T> and implement the IMustHaveAnAccountId interface.  Note, abstract behaviors are implemented using Func<dynamic,bool> or Action<dynamic> which means that when setting up the configuration you will not have intellisense for the properties of the target type.

## Configuration/Initialization
Configuration for NoRMatic is done via the static methods of NoRMaticModel<T> and NoRMaticConfig.  Executing the configurations is a simple matter of creating a class which implements INoRMaticInitializer.  The Setup() methods of any class implementing this interface will be executed when the NoRMaticConfig.Initialize() method is called, in a web application you might place the call to Initialize() in your Global.asax Application_Start event which would setup NoRMatic for the session.  For example:
	
	// Somewhere in your project
	public class NoRMaticSetup : INoRMaticInitializer {
		public void Setup() {
			
			NoRMaticConfig.SetConnectionStringProvider(() => GetConnectionStringFromSomewhere());
			
			Customer.EnableSoftDelete();
			Customer.EnableVersioning();
			Customer.AddQueryBehavior(x => x.AccountID == Session["AccountID"]);
			
			Product.EnableSoftDelete();
			
			Order.EnableSoftDelete();
			Order.EnableVersioning();
			
			Order.AddIndex(x => x.ProductId);
		}
	}
	
	// In you Global.asax
	public static void Application_Start() {
		NoRMaticConfig.Initialize();
	}
	
### Connection String Provider
If you do not configure a connection string provider, NoRMatic will look for a connection string called 'NoRMaticConnectionString'.  The connection string must be a valid MongoDB connection string such as:

	mongodb://<servername>/<databasename>?strict=true
	
If you need to confiture another mechanism for retrieving the connection string you can set a global connection string provider function, or set one per model, or both.  The model level connection string provider will override any global providers set.  Connection string providers, like behaviors, are simply expressed as anonymous functions whcih return strings.  For example:

	NoRMaticConfig.SetConnectionStringProvider(() => ConfigurationManager.AppSettings["SomeOtherConnectionString"]);
	
	Product.SetConnectionStringProvider(() => "mongodb://localhost/ProductCatalog");
	Customer.SetConnectionStringProvider(() => string.Format("mongodb://localhost/{0}", Session["AccountName"]));
	
In these examples you can see how it's easy to substitue the default expected connection string name with a provider that will return a runtime connection string based on your applications context.

### Current User Provider


### Indexing

