var CommentsExchange=function() {
CommentsExchange.initializeBase(this);
this._timeout = 0;
this._userContext = null;
this._succeeded = null;
this._failed = null;
}
CommentsExchange.prototype={
_get_path:function() {
 var p = this.get_path();
 if (p) return p;
 else return CommentsExchange._staticInstance.get_path();},
AddComment:function(commentId,commentText,succeededCallback, failedCallback, userContext) {
return this._invoke(this._get_path(), 'AddComment',false,{commentId:commentId,commentText:commentText},succeededCallback,failedCallback,userContext); },
DeleteComment:function(commentId,succeededCallback, failedCallback, userContext) {
return this._invoke(this._get_path(), 'DeleteComment',false,{commentId:commentId},succeededCallback,failedCallback,userContext); },
GetNumberOfReviewsWhereIAmAReviewer:function(succeededCallback, failedCallback, userContext) {
return this._invoke(this._get_path(), 'GetNumberOfReviewsWhereIAmAReviewer',false,{},succeededCallback,failedCallback,userContext); },
GetNumberOfReviewsWhereIAmTheReviewee:function(succeededCallback, failedCallback, userContext) {
return this._invoke(this._get_path(), 'GetNumberOfReviewsWhereIAmTheReviewee',false,{},succeededCallback,failedCallback,userContext); },
GetNumberOfOpenReviews:function(succeededCallback, failedCallback, userContext) {
return this._invoke(this._get_path(), 'GetNumberOfOpenReviews',false,{},succeededCallback,failedCallback,userContext); }}
CommentsExchange.registerClass('CommentsExchange',Sys.Net.WebServiceProxy);
CommentsExchange._staticInstance = new CommentsExchange();
CommentsExchange.set_path = function(value) { CommentsExchange._staticInstance.set_path(value); }
CommentsExchange.get_path = function() { return CommentsExchange._staticInstance.get_path(); }
CommentsExchange.set_timeout = function(value) { CommentsExchange._staticInstance.set_timeout(value); }
CommentsExchange.get_timeout = function() { return CommentsExchange._staticInstance.get_timeout(); }
CommentsExchange.set_defaultUserContext = function(value) { CommentsExchange._staticInstance.set_defaultUserContext(value); }
CommentsExchange.get_defaultUserContext = function() { return CommentsExchange._staticInstance.get_defaultUserContext(); }
CommentsExchange.set_defaultSucceededCallback = function(value) { CommentsExchange._staticInstance.set_defaultSucceededCallback(value); }
CommentsExchange.get_defaultSucceededCallback = function() { return CommentsExchange._staticInstance.get_defaultSucceededCallback(); }
CommentsExchange.set_defaultFailedCallback = function(value) { CommentsExchange._staticInstance.set_defaultFailedCallback(value); }
CommentsExchange.get_defaultFailedCallback = function() { return CommentsExchange._staticInstance.get_defaultFailedCallback(); }
CommentsExchange.set_path("/malevich/commentsexchange.asmx");
CommentsExchange.AddComment= function(commentId,commentText,onSuccess,onFailed,userContext) {CommentsExchange._staticInstance.AddComment(commentId,commentText,onSuccess,onFailed,userContext); }
CommentsExchange.DeleteComment= function(commentId,onSuccess,onFailed,userContext) {CommentsExchange._staticInstance.DeleteComment(commentId,onSuccess,onFailed,userContext); }
CommentsExchange.GetNumberOfReviewsWhereIAmAReviewer= function(onSuccess,onFailed,userContext) {CommentsExchange._staticInstance.GetNumberOfReviewsWhereIAmAReviewer(onSuccess,onFailed,userContext); }
CommentsExchange.GetNumberOfReviewsWhereIAmTheReviewee= function(onSuccess,onFailed,userContext) {CommentsExchange._staticInstance.GetNumberOfReviewsWhereIAmTheReviewee(onSuccess,onFailed,userContext); }
CommentsExchange.GetNumberOfOpenReviews= function(onSuccess,onFailed,userContext) {CommentsExchange._staticInstance.GetNumberOfOpenReviews(onSuccess,onFailed,userContext); }
