﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Norm;
using Norm.Attributes;
using Norm.BSON.DbTypes;

namespace NoRMatic {

    /// <summary>
    /// This is the base class for all NoRMatic models.
    /// </summary>
    public abstract partial class NoRMaticModel<T> where T : NoRMaticModel<T> {

        internal static GlobalConfigContainer GlobalConfig {
            get { return GlobalConfigContainer.Instance; }
        }

        internal static ModelConfigContainer<T> ModelConfig {
            get { return ModelConfigContainer<T>.Instance; }
        }

        /// <summary>
        /// Gets or sets the identifier for the model.  This will be auto-assigned when Save() is called on a
        /// new model.
        /// </summary>
        public ObjectId Id { get; set; }

        /// <summary>
        /// Gets the created date for the model.  This will be auto-assigned when Save() is called on the model
        /// for the first time.
        /// </summary>
        public DateTime DateCreated { get; internal set; }

        /// <summary>
        /// Gets or sets the last updated date for the model.  This will be auto-assigned when Save() is called
        /// on the model.  If EnableVersioning is set, this will also indicate the date the version was made for
        /// version documents.
        /// </summary>
        public DateTime DateUpdated { get; set; }

        /// <summary>
        /// Returns any validation errors for the current state of the entity.  Validation uses the System.ComponentModel.DataAnnotations
        /// attributes along with NoRMatic's [ValidateChild] attribute which allows deep validation of custom nested types.
        /// </summary>
        [MongoIgnore]
        public List<ValidationResult> Errors {
            get { return Validate(); }
        }

        /// <summary>
        /// Deletes all documents from a collection.  NOTE: This does NOT respect the EnableSoftDelete behavior, if
        /// called DeleteAll() will permanently remove all documents from the collection.
        /// </summary>
        public static void DeleteAll() {

            var connectionString = ModelConfig.ConnectionStringProvider != null ?
                ModelConfig.ConnectionStringProvider() : NoRMaticConfig.ConnectionString;

            using (var db = Mongo.Create(connectionString)) {
                // Try/Catch is to avoid error when DeleteAll() is called on a non-existant collection
                try { db.Database.DropCollection(typeof(T).Name); } catch { }
                WriteToLog(string.Format("DELETE ALL -- Type: {0}", typeof(T).Name));
            }
        }

        /// <summary>
        /// Creates or saves the entity to the database.  If the EnableVersioning behavior is set then a version
        /// will be created for each save.  NOTE: Versions are created regardless of whether changes exist or not.
        /// </summary>
        public virtual T Save() {

            if (ModelConfig.EnableSoftDelete && IsDeleted) return (T)this;
            if (ModelConfig.EnableVersioning && IsVersion) return (T)this;

            if (!DoBeforeBehaviors(
                GlobalConfig.BeforeSave.GetByType(GetType()),
                ModelConfig.BeforeSave)) return (T)this;

            if (Validate().Count > 0) return (T)this;

            if (DateCreated == default(DateTime))
                DateCreated = DateTime.Now;

            DateUpdated = DateTime.Now;

            if (ModelConfig.EnableUserAuditing && GlobalConfig.CurrentUserProvider != null)
                UpdatedBy = GlobalConfig.CurrentUserProvider();

            SyncSave((T)this);

            if (ModelConfig.EnableVersioning) SaveVersion();

            DoAfterBehaviors(
                GlobalConfig.AfterSave.GetByType(GetType()),
                ModelConfig.AfterSave);

            WriteToLog(string.Format("SAVED -- Type: {0}, Id: {1}", typeof(T).Name, Id));

            return (T)this;
        }

        /// <summary>
        /// Deletes or sets IsDeleted flag for the entity depending on whether or not the EnableSoftDelete
        /// behavior is set for this type.
        /// </summary>
        public virtual void Delete() {

            if (!DoBeforeBehaviors(
                GlobalConfig.BeforeDelete.GetByType(GetType()),
                ModelConfig.BeforeDelete)) return;

            if (ModelConfig.EnableSoftDelete) {
                SoftDelete();
            } else {
                if (ModelConfig.EnableVersioning) DeleteVersions();
                GetMongoCollection().Delete((T)this);
                WriteToLog(string.Format("DELETE -- Type: {0}, Id: {1}", typeof(T).Name, Id));
            }

            DoAfterBehaviors(
                GlobalConfig.AfterDelete.GetByType(GetType()),
                ModelConfig.AfterDelete);
        }

        /// <summary>
        /// Retrieves the value of a DbReference from the database.  NOTE: The reference type's connection string
        /// provider will be used, not the current types provider.
        /// </summary>
        public TRef GetRef<TRef>(Func<T, DbReference<TRef>> refProperty) 
            where TRef : NoRMaticModel<TRef>, new() {

            var dbRef = refProperty((T)this);
            return GetMongoCollection<TRef>().FindOne(new { _id = dbRef.Id });
        }

        /// <summary>
        /// Returns all previous versions of the entity if the EnableVersioning flag is set for this type.
        /// </summary>
        public virtual IEnumerable<T> GetVersions() {
            return GetMongoCollection().Find(new { IsVersion = true, VersionOfId = Id })
                .OrderByDescending(x => x.DateUpdated);
        }

        private List<ValidationResult> Validate() {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(this, new ValidationContext(this, null, null), results, true);
            return results;
        }

        private void SoftDelete() {
            IsDeleted = true;
            DateDeleted = DateTime.Now;
            SyncSave((T) this);
            WriteToLog(string.Format("SOFT DELETE -- Type: {0}, Id: {1}", typeof(T).Name, Id));
        }

        private void SaveVersion() {
            var clone = Clone();
            clone.IsVersion = true;
            clone.VersionOfId = Id;
            SyncSave(clone);
            WriteToLog(string.Format("VERSIONED -- Type: {0}, Source Id: {1}, Version Id: {2}", typeof(T).Name, Id, clone.Id));
        }

        private T Clone() {
            var obj = GetMongoCollection().FindOne(new { Id });
            obj.Id = null;
            return obj;
        }

        private void DeleteVersions() {
            var versions = GetMongoCollection().Find(new { VersionOfId = Id }).ToList();
            for (var i = 0; i < versions.Count(); i++)
                GetMongoCollection().Delete(versions[i]);
        }

        private bool DoBeforeBehaviors(
            IEnumerable<Func<dynamic, bool>> global, IEnumerable<Func<T, bool>> model) {

            var all = true;

            if (global.Any(x => x(this) == false)) all = false;
            if (model.Any(x => x((T)this) == false)) all = false;

            return all;
        }

        private void DoAfterBehaviors(
            List<Action<dynamic>> global, List<Action<T>> model) {

            global.ForEach(x => x(this));
            model.ForEach(x => x((T)this));
        }

        private static void WriteToLog(string message) {
            if (GlobalConfig.LogListener != null)
                GlobalConfig.LogListener(message);
        }

        private static void SyncSave(T obj) {
            var db = GetDatabase();
            db.GetCollection<T>().Save(obj);
            db.LastError(); // Forces the write to be synchronous
        }
    }
}
