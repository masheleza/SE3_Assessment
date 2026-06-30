import { HttpInterceptorFn } from '@angular/common/http';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // Production: retrieve from your AuthService / OIDC library
  const token = localStorage.getItem('access_token');
  if (!token) return next(req);

  return next(req.clone({
    setHeaders: { Authorization: `Bearer ${token}` }
  }));
};
