import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { App, Overview, Registry, DiscoveryPage, HistoryPage } from './app/platform';
bootstrapApplication(App,{providers:[provideHttpClient(),provideRouter([
  {path:'',component:Overview},{path:'bots',component:Registry},{path:'discovery',component:DiscoveryPage},{path:'history',component:HistoryPage},{path:'**',redirectTo:''}
])]}).catch(console.error);
